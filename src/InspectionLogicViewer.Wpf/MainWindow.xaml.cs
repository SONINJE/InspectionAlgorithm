using Microsoft.Win32;
using System.IO;
using Path = System.IO.Path;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Collections.Generic;
using System;
using System.Windows.Input;
using System.Text.Json;

namespace InspectionLogicViewer.Wpf;

public partial class MainWindow : Window
{
    private BitmapSource? _source;
    private readonly List<DefectResult> _results = new();

    // Zoom 관련 필드
    private readonly ScaleTransform _logicScale = new(1.0, 1.0);
    private readonly TranslateTransform _logicTranslate = new(0.0, 0.0);
    private const double ZoomFactor = 1.1;
    private const double MinZoom = 0.2;
    private const double MaxZoom = 4.0;

    // Pan(잡고 끌기) 관련 필드
    private bool _isPanning = false;
    private Point _panStart;
    private double _panOriginX, _panOriginY;

    // 파라미터 (기본값)
    private double _D_FORM_MIN_AREA_RATIO = 0.25;
    private double _D_ROUNDNESS = 0.60;
    private double _D_DARK_AREA_PERCENT = 0.05;
    private double _D_LINEAR_BASE_BRIGHT = 40.0;
    private double _D_LINE_ANGLE_LOW = 10.0;
    private double _D_LINE_ANGLE_HIGH = 90.0;
    private double _D_WHITE_PEAK_IF = 60.0;
    private double _D_WHITE_PEAK_ELSEIF = 40.0;
    private double _D_WHITE_RATIO = 1.50;
    private double _D_WHITE_LINE_PEAK = 50.0;
    private double _D_LINEARITY_RATIO = 3.0;
    private int _AREA_MIN = 4;
    private int _NDIL_CNT = 2;

    // 여유값(Margin) 표시 임계치
    private const double MarginDangerPercent = 5.0;   // 5% 미만: 위험(경계값 매우 근접)
    private const double MarginCautionPercent = 15.0; // 15% 미만: 주의

    private readonly string _paramsFilePath = Path.Combine(AppContext.BaseDirectory, "inspect_params.json");

    public MainWindow()
    {
        InitializeComponent();

        var tg = new TransformGroup();
        tg.Children.Add(_logicScale);
        tg.Children.Add(_logicTranslate);
        LogicCanvas.RenderTransform = tg;
        LogicCanvas.RenderTransformOrigin = new Point(0, 0);

        // 마우스 이벤트 연결 (패닝)
        LogicCanvas.MouseLeftButtonDown += LogicCanvas_MouseLeftButtonDown;
        LogicCanvas.MouseMove += LogicCanvas_MouseMove;
        LogicCanvas.MouseLeftButtonUp += LogicCanvas_MouseLeftButtonUp;
        LogicCanvas.MouseLeave += LogicCanvas_MouseLeave;

        try { LoadParamsFromJson(); } catch { }
        UpdateParamSummaryPanel();

        // 초기 상태: overlay 캔버스 크기 동기화
        ImageOverlayCanvas.Width = ImageView.ActualWidth;
        ImageOverlayCanvas.Height = ImageView.ActualHeight;

        ImageView.SizeChanged += (s, e) =>
        {
            ImageOverlayCanvas.Width = ImageView.ActualWidth;
            ImageOverlayCanvas.Height = ImageView.ActualHeight;
        };
    }

    private void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Image|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff" };
        if (d.ShowDialog() != true) return;

        using var s = File.OpenRead(d.FileName);
        var decoder = BitmapDecoder.Create(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        _source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgr32, null, 0);
        ImageView.Source = _source;
        _results.Clear(); DefectList.Items.Clear(); LogicTreeView.Items.Clear();
        StatusText.Text = $"{Path.GetFileName(d.FileName)}  {_source.PixelWidth} x {_source.PixelHeight}";
        ClearImageOverlay();
    }

    // ScrollViewer의 PreviewMouseWheel 이벤트: 마우스 위치 기준으로 줌 처리
    private void LogicScroll_PreviewMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        double oldScale = _logicScale.ScaleX;
        double zoom = Math.Pow(ZoomFactor, e.Delta / 120.0); // Delta는 보통 120 단위
        double newScale = Math.Clamp(oldScale * zoom, MinZoom, MaxZoom);
        double scaleRatio = newScale / oldScale;
        if (Math.Abs(newScale - oldScale) < 1e-6) { e.Handled = true; return; }

        Point mousePos = e.GetPosition(LogicCanvas);

        _logicTranslate.X = mousePos.X - (mousePos.X - _logicTranslate.X) * scaleRatio;
        _logicTranslate.Y = mousePos.Y - (mousePos.Y - _logicTranslate.Y) * scaleRatio;

        _logicScale.ScaleX = newScale;
        _logicScale.ScaleY = newScale;

        e.Handled = true;
    }

    // 패닝 시작 (좌클릭)
    private void LogicCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        _panStart = e.GetPosition(this); // 윈도우 좌표계
        _panOriginX = _logicTranslate.X;
        _panOriginY = _logicTranslate.Y;
        LogicCanvas.CaptureMouse();
        LogicCanvas.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void LogicCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;

        Point now = e.GetPosition(this); // 윈도우 좌표계
        Vector delta = now - _panStart;

        double invScale = (_logicScale.ScaleX != 0.0) ? 1.0 / _logicScale.ScaleX : 1.0;
        _logicTranslate.X = _panOriginX + delta.X * invScale;
        _logicTranslate.Y = _panOriginY + delta.Y * invScale;

        e.Handled = true;
    }

    private void LogicCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        LogicCanvas.ReleaseMouseCapture();
        LogicCanvas.Cursor = Cursors.Arrow;
        e.Handled = true;
    }

    private void LogicCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        if (Mouse.LeftButton != MouseButtonState.Pressed)
        {
            _isPanning = false;
            LogicCanvas.ReleaseMouseCapture();
            LogicCanvas.Cursor = Cursors.Arrow;
        }
    }

    private void Inspect_Click(object sender, RoutedEventArgs e)
    {
        if (_source == null) { MessageBox.Show("먼저 이미지를 열어주세요."); return; }
        if (!int.TryParse(ThresholdBox.Text, out int threshold)) threshold = 35;

        int stride = _source.PixelWidth * 4;
        byte[] pixels = new byte[stride * _source.PixelHeight];
        _source.CopyPixels(pixels, stride, 0);
        var native = new DefectResultNative[256];

        var h = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        int count;
        try
        {
            count = InspectImage(h.AddrOfPinnedObject(), _source.PixelWidth, _source.PixelHeight,
                stride, threshold, native, native.Length);
        }
        catch (DllNotFoundException ex)
        {
            MessageBox.Show($"네이티브 DLL을 찾을 수 없습니다: {ex.Message}");
            h.Free();
            return;
        }
        catch (BadImageFormatException ex)
        {
            MessageBox.Show($"네이티브 DLL 아키텍처 불일치: {ex.Message}");
            h.Free();
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"검사 중 오류: {ex.Message}");
            h.Free();
            return;
        }
        finally
        {
            if (h.IsAllocated) h.Free();
        }

        _results.Clear(); DefectList.Items.Clear();
        for (int i = 0; i < count; i++)
        {
            var r = native[i].ToManaged();
            _results.Add(r);
            DefectList.Items.Add($"#{i + 1}  {r.DefectTypeName}  ({r.X},{r.Y})  {r.Width} x {r.Height}");
        }
        StatusText.Text = $"검사 완료 : {_results.Count}개";
        if (_results.Count > 0) DefectList.SelectedIndex = 0;
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_results.Count > 0) DefectList.SelectedIndex = Math.Max(0, DefectList.SelectedIndex - 1);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_results.Count > 0) DefectList.SelectedIndex = Math.Min(_results.Count - 1, DefectList.SelectedIndex + 1);
    }

    private void DefectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int i = DefectList.SelectedIndex;
        if (i < 0 || i >= _results.Count) return;
        var r = _results[i];

        Fx.Text = r.X.ToString();
        Fy.Text = r.Y.ToString();
        Fw.Text = r.Width.ToString();
        Fh.Text = r.Height.ToString();
        Farea.Text = r.Area.ToString("F0");
        Fmean.Text = r.Mean.ToString("F1");
        Faspect.Text = r.AspectRatio.ToString("F2");
        Ftype.Text = r.DefectTypeName;

        FDefectCode.Text = ((int)r.DefectType).ToString();
        FIsDark.Text = r.IsDark ? "TRUE" : "FALSE";
        FIsLinear.Text = r.IsLinear ? "TRUE" : "FALSE";

        FareaRatio.Text = r.AreaRatio.ToString("F3");
        Fcircularity.Text = r.Circularity.ToString("F3");
        FangleDeg.Text = r.AngleDeg.ToString("F1");
        FpeakMax.Text = r.PeakMax.ToString("F1");
        FareaObjPercent.Text = r.AreaObjPercent.ToString("F6");
        FratioMopol.Text = r.RatioMopol.ToString("F3");

        BuildLogicTree(r);

        // ROI 오버레이 그리기
        DrawSelectedROIOnImage(r);
    }

    // ─────────────────────────────────────────────────────────
    // 여유값(Margin) 계산 및 표시 헬퍼
    // ─────────────────────────────────────────────────────────

    // 단순 비교(">" 또는 "<")의 여유값 계산
    // margin: 실제값이 기준선을 넘은 절대량 (+ 면 조건 충족 방향으로 여유, - 면 미달)
    // marginPercent: 기준값 대비 상대 비율(%)
    private static (double margin, double marginPercent) ComputeMargin(double actual, double threshold, string op)
    {
        double margin = (op == "<" || op == "<=")
            ? threshold - actual   // 작아야 충족되는 조건
            : actual - threshold;  // 커야 충족되는 조건

        double basis = Math.Abs(threshold) > 1e-9 ? Math.Abs(threshold)
                     : Math.Abs(actual) > 1e-9 ? Math.Abs(actual)
                     : 1.0;
        double marginPercent = margin / basis * 100.0;
        return (margin, marginPercent);
    }

    // 여유 정도에 따른 표시 색상/아이콘
    private static (Brush color, string icon) GetMarginStyle(double marginPercent)
    {
        double abs = Math.Abs(marginPercent);
        if (abs < MarginDangerPercent) return (Brushes.OrangeRed, "⚠ ");
        if (abs < MarginCautionPercent) return (Brushes.DarkGoldenrod, "△ ");
        return (Brushes.SeaGreen, "");
    }

    // 여유값 텍스트 한 줄 생성 (공통)
    private static TextBlock BuildMarginLine(double margin, double marginPercent)
    {
        var (color, icon) = GetMarginStyle(marginPercent);
        string sign = margin >= 0 ? "+" : "";
        return new TextBlock
        {
            Text = $"{icon}여유: {sign}{margin:F2}  ({sign}{marginPercent:F1}%)",
            FontSize = 11,
            FontWeight = Math.Abs(marginPercent) < MarginCautionPercent ? FontWeights.Bold : FontWeights.Normal,
            Foreground = color
        };
    }

    // ── 파라미터 비교 노드 생성 헬퍼들 ────────────────────────────

    // 단순 비교 (실제값 [연산자] 파라미터값)
    private static TreeViewItem AddParamCompare(
        TreeViewItem parent, string title,
        double actualValue, string actualName,
        string op,
        double paramValue, string paramName,
        bool result, string format = "F3")
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{actualName} = {actualValue.ToString(format)}   {op}   {paramName} = {paramValue.ToString(format)}",
            FontSize = 11,
            Foreground = Brushes.DimGray
        });

        var (margin, marginPercent) = ComputeMargin(actualValue, paramValue, op);
        panel.Children.Add(BuildMarginLine(margin, marginPercent));

        panel.Children.Add(new TextBlock
        {
            Text = result ? "→ TRUE" : "→ FALSE",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = result ? Brushes.DarkGreen : Brushes.IndianRed
        });

        var item = new TreeViewItem { Header = panel, IsExpanded = true };
        parent.Items.Add(item);
        return item;
    }

    // 범위 비교 (실제값 < 하한 또는 실제값 > 상한) — 각도 조건용
    private static TreeViewItem AddRangeCompare(
        TreeViewItem parent, string title,
        double actualValue, string actualName,
        double lowParam, string lowName,
        double highParam, string highName,
        bool result, string format = "F1")
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{actualName} = {actualValue.ToString(format)}   " +
                   $"(< {lowName}={lowParam.ToString(format)}  또는  > {highName}={highParam.ToString(format)})",
            FontSize = 11,
            Foreground = Brushes.DimGray
        });

        // OR 조건: 하한/상한 각각에서 실제값이 얼마나 벗어났는지(+) / 벗어나려면 얼마나 남았는지(-)
        double marginLow = (actualValue < lowParam)
            ? lowParam - actualValue
            : -(actualValue - lowParam);

        double marginHigh = (actualValue > highParam)
            ? actualValue - highParam
            : -(highParam - actualValue);

        // 두 경계 중 조건에 더 크게 기여한(가까운) 쪽을 대표 여유값으로 사용
        double margin = Math.Max(marginLow, marginHigh);
        double basis = Math.Abs(actualValue) > 1e-9 ? Math.Abs(actualValue) : 1.0;
        double marginPercent = margin / basis * 100.0;

        panel.Children.Add(BuildMarginLine(margin, marginPercent));

        panel.Children.Add(new TextBlock
        {
            Text = result ? "→ TRUE" : "→ FALSE",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = result ? Brushes.DarkGreen : Brushes.IndianRed
        });

        var item = new TreeViewItem { Header = panel, IsExpanded = true };
        parent.Items.Add(item);
        return item;
    }

    // 구간(Between) 비교 (하한 < 실제값 <= 상한) — WHITE_PEAK ElseIf 조건용
    private static TreeViewItem AddBetweenCompare(
        TreeViewItem parent, string title,
        double actualValue, string actualName,
        double lowParam, string lowName,
        double highParam, string highName,
        bool result, string format = "F1")
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{lowName}={lowParam.ToString(format)}  <  {actualName}={actualValue.ToString(format)}  <=  {highName}={highParam.ToString(format)}",
            FontSize = 11,
            Foreground = Brushes.DimGray
        });

        // AND(구간) 조건: 실제값이 구간 안쪽으로 얼마나 여유 있는지 → 두 경계까지 거리 중 더 짧은(타이트한) 쪽
        double distToLow = actualValue - lowParam;   // 양수면 하한 통과
        double distToHigh = highParam - actualValue; // 양수면 상한 통과
        double margin = Math.Min(distToLow, distToHigh);

        double basis = Math.Abs(actualValue) > 1e-9 ? Math.Abs(actualValue) : 1.0;
        double marginPercent = margin / basis * 100.0;

        panel.Children.Add(BuildMarginLine(margin, marginPercent));

        panel.Children.Add(new TextBlock
        {
            Text = result ? "→ TRUE" : "→ FALSE",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = result ? Brushes.DarkGreen : Brushes.IndianRed
        });

        var item = new TreeViewItem { Header = panel, IsExpanded = true };
        parent.Items.Add(item);
        return item;
    }

    private void BuildLogicTree(DefectResult r)
    {
        LogicTreeView.Items.Clear();
        var root = new TreeViewItem { Header = "ROOT", IsExpanded = true, FontWeight = FontWeights.Bold };
        LogicTreeView.Items.Add(root);

        bool isDark = r.IsDark;
        bool isLinear = r.IsLinear;

        var nodeDark = AddCondition(root, "다크성 판정", isDark ? "TRUE" : "FALSE", isDark);

        if (isDark)
        {
            var nodeLine = AddCondition(nodeDark, "라인성 판정", isLinear ? "TRUE" : "FALSE", isLinear);

            if (!isLinear)
            {
                // ── 다크 & 비선형: 분화구 / 크랙 / 흑 약불량(WEAK_POINT_D) ──
                bool condFormArea = r.AreaRatio > _D_FORM_MIN_AREA_RATIO;
                var a = AddParamCompare(nodeLine, "형태 판단: 면적비율이 최소 기준보다 큰가",
                    r.AreaRatio, "AreaRatio", ">", _D_FORM_MIN_AREA_RATIO, "D_FORM_MIN_AREA_RATIO", condFormArea);

                if (!condFormArea)
                {
                    AddResult(a, "흑 약불량 (WEAK_POINT_D)");
                }
                else
                {
                    bool condRoundness = r.Circularity > _D_ROUNDNESS;
                    var b = AddParamCompare(a, "형태 판단: 원형도가 흑 원형성 기준보다 큰가",
                        r.Circularity, "Circularity", ">", _D_ROUNDNESS, "D_ROUNDNESS", condRoundness);

                    if (!condRoundness)
                    {
                        AddResult(b, "크랙 (CRACK)");
                    }
                    else
                    {
                        bool condDarkAreaPct = r.AreaObjPercent > _D_DARK_AREA_PERCENT;
                        var c = AddParamCompare(b, "면적 비중: 결함 면적%가 Dark 면적% 기준보다 큰가",
                            r.AreaObjPercent, "AreaObjPercent", ">", _D_DARK_AREA_PERCENT, "D_DARK_AREA_PERCENT", condDarkAreaPct, "F6");

                        if (condDarkAreaPct)
                            AddResult(c, "분화구 (CRATER)");
                        else
                            AddResult(c, "크랙 (CRACK)");
                    }
                }
            }
            else
            {
                // ── 다크 & 선형: 스크래치 / 크랙(PARTICLE) / 흑 약불량(BLACK_WEAK) ──
                bool condDarkPeak = r.PeakMax > _D_LINEAR_BASE_BRIGHT;
                var a = AddParamCompare(nodeLine, "밝기 판단: 다크 피크치가 선형 불량 기준밝기보다 큰가",
                    r.PeakMax, "PeakMax", ">", _D_LINEAR_BASE_BRIGHT, "D_LINEAR_BASE_BRIGHT", condDarkPeak, "F1");

                if (!condDarkPeak)
                {
                    AddResult(a, "흑 약불량 (BLACK_WEAK)");
                }
                else
                {
                    bool condAngle = r.AngleDeg < _D_LINE_ANGLE_LOW || r.AngleDeg > _D_LINE_ANGLE_HIGH;
                    var b = AddRangeCompare(a, "각도 판단: 불량 각도가 정상 범위를 벗어났는가",
                        r.AngleDeg, "AngleDeg", _D_LINE_ANGLE_LOW, "D_LINE_ANGLE_LOW", _D_LINE_ANGLE_HIGH, "D_LINE_ANGLE_HIGH", condAngle);

                    if (condAngle)
                        AddResult(b, "스크래치 (SCRATCH)");
                    else
                        AddResult(b, "크랙 (PARTICLE)");
                }
            }
        }
        else
        {
            var nodeWhite = AddCondition(root, "화이트성 판정", "TRUE", true);
            var nodeLineW = AddCondition(nodeWhite, "라인성 판정", isLinear ? "TRUE" : "FALSE", isLinear);

            if (!isLinear)
            {
                // ── 화이트 & 비선형: 핀홀 / 미세긁힘 / 찍힘 / 화이트 약불량 ──
                bool condIf = r.PeakMax > _D_WHITE_PEAK_IF;
                var ifNode = AddParamCompare(nodeLineW, "PEAK 판단(1단계): 불량 피크치가 IF 기준값보다 큰가",
                    r.PeakMax, "PeakMax", ">", _D_WHITE_PEAK_IF, "D_WHITE_PEAK_IF", condIf, "F1");

                if (condIf)
                {
                    AddResult(ifNode, "핀홀 (PINHOLE)");
                }
                else
                {
                    bool condElseIf = r.PeakMax > _D_WHITE_PEAK_ELSEIF;
                    var elifNode = AddParamCompare(ifNode, "PEAK 판단(2단계): 불량 피크치가 ELSEIF 기준값보다 큰가",
                        r.PeakMax, "PeakMax", ">", _D_WHITE_PEAK_ELSEIF, "D_WHITE_PEAK_ELSEIF", condElseIf, "F1");

                    if (condElseIf)
                    {
                        bool condRatio = r.RatioMopol > _D_WHITE_RATIO;
                        var ratioNode = AddParamCompare(elifNode, "모폴로지 판단: 비율값이 화이트 비율 기준보다 큰가",
                            r.RatioMopol, "RatioMopol", ">", _D_WHITE_RATIO, "D_WHITE_RATIO", condRatio, "F3");

                        if (condRatio)
                            AddResult(ratioNode, "미세긁힘 (MICRO_SCRATCH)");
                        else
                            AddResult(ratioNode, "찍힘 (DENT)");
                    }
                    else
                    {
                        AddResult(elifNode, "화이트 약불량 (WHITE_WEAK)");
                    }
                }
            }
            else
            {
                // ── 화이트 & 선형: 라인 / 미세긁힘(STAIN) ──
                bool condWhiteLine = r.PeakMax < _D_WHITE_LINE_PEAK;
                var a = AddParamCompare(nodeLineW, "라인 판단: 불량 피크치가 화이트 라인 기준보다 작은가",
                    r.PeakMax, "PeakMax", "<", _D_WHITE_LINE_PEAK, "D_WHITE_LINE_PEAK", condWhiteLine, "F1");

                if (condWhiteLine)
                    AddResult(a, "라인 (LINE)");
                else
                    AddResult(a, "미세긁힘 (STAIN)");
            }
        }

        DrawLogicDiagramInCanvas(LogicCanvas, r);
    }

    private static TreeViewItem AddCondition(TreeViewItem parent, string text, object actual, bool result)
    {
        var item = new TreeViewItem
        {
            Header = $"{text}  [{(result ? "TRUE" : "FALSE")}]  Actual={actual}",
            IsExpanded = true,
            Foreground = result ? Brushes.DarkGreen : Brushes.Gray,
            FontWeight = result ? FontWeights.Bold : FontWeights.Normal
        };
        parent.Items.Add(item); return item;
    }

    private static void AddResult(TreeViewItem parent, string text) =>
        parent.Items.Add(new TreeViewItem { Header = "▶ " + text, Foreground = Brushes.DarkBlue, FontWeight = FontWeights.Bold });

    [DllImport("InspectionAlgorithm.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int InspectImage(IntPtr image, int width, int height, int stride, int threshold,
        [Out] DefectResultNative[] results, int maxResults);

    // 하단 파라미터 요약 패널 갱신
    private void UpdateParamSummaryPanel()
    {
        PText_FormMinAreaRatio.Text = _D_FORM_MIN_AREA_RATIO.ToString("F3");
        PText_Roundness.Text = _D_ROUNDNESS.ToString("F3");
        PText_DarkAreaPercent.Text = _D_DARK_AREA_PERCENT.ToString("F3");
        PText_LinearBaseBright.Text = _D_LINEAR_BASE_BRIGHT.ToString("F1");
        PText_LineAngleLow.Text = _D_LINE_ANGLE_LOW.ToString("F1");
        PText_LineAngleHigh.Text = _D_LINE_ANGLE_HIGH.ToString("F1");
        PText_WhitePeakIf.Text = _D_WHITE_PEAK_IF.ToString("F1");
        PText_WhitePeakElseIf.Text = _D_WHITE_PEAK_ELSEIF.ToString("F1");
        PText_WhiteRatio.Text = _D_WHITE_RATIO.ToString("F2");
        PText_WhiteLinePeak.Text = _D_WHITE_LINE_PEAK.ToString("F1");
        PText_LinearityRatio.Text = _D_LINEARITY_RATIO.ToString("F1");
        PText_AreaMin.Text = _AREA_MIN.ToString();
        PText_NdilCnt.Text = _NDIL_CNT.ToString();
    }

    // 파라미터 다이얼로그 열기
    private void OpenParamsDialog_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ParamDialog(
            _D_FORM_MIN_AREA_RATIO,
            _D_ROUNDNESS,
            _D_DARK_AREA_PERCENT,
            _D_LINEAR_BASE_BRIGHT,
            _D_LINE_ANGLE_LOW,
            _D_LINE_ANGLE_HIGH,
            _D_WHITE_PEAK_IF,
            _D_WHITE_PEAK_ELSEIF,
            _D_WHITE_RATIO,
            _D_WHITE_LINE_PEAK,
            _D_LINEARITY_RATIO,
            _AREA_MIN,
            _NDIL_CNT)
        {
            Owner = this
        };

        if (dlg.ShowDialog() == true)
        {
            _D_FORM_MIN_AREA_RATIO = dlg.D_FORM_MIN_AREA_RATIO;
            _D_ROUNDNESS = dlg.D_ROUNDNESS;
            _D_DARK_AREA_PERCENT = dlg.D_DARK_AREA_PERCENT;
            _D_LINEAR_BASE_BRIGHT = dlg.D_LINEAR_BASE_BRIGHT;
            _D_LINE_ANGLE_LOW = dlg.D_LINE_ANGLE_LOW;
            _D_LINE_ANGLE_HIGH = dlg.D_LINE_ANGLE_HIGH;
            _D_WHITE_PEAK_IF = dlg.D_WHITE_PEAK_IF;
            _D_WHITE_PEAK_ELSEIF = dlg.D_WHITE_PEAK_ELSEIF;
            _D_WHITE_RATIO = dlg.D_WHITE_RATIO;
            _D_WHITE_LINE_PEAK = dlg.D_WHITE_LINE_PEAK;
            _D_LINEARITY_RATIO = dlg.D_LINEARITY_RATIO;
            _AREA_MIN = dlg.AREA_MIN;
            _NDIL_CNT = dlg.NDIL_CNT;

            try
            {
                SaveParamsToJson();
                UpdateParamSummaryPanel();
                StatusText.Text = "파라미터 저장됨";
                if (DefectList.SelectedIndex >= 0 && DefectList.SelectedIndex < _results.Count)
                    DrawLogicDiagramInCanvas(LogicCanvas, _results[DefectList.SelectedIndex]);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"파라미터 저장 실패: {ex.Message}");
            }
        }
    }

    private void Crop_Show_Click(object sender, RoutedEventArgs e)
    {
        int i = DefectList.SelectedIndex;
        if (i < 0 || i >= _results.Count) { MessageBox.Show("먼저 불량을 선택하세요."); return; }
        if (_source == null) { MessageBox.Show("이미지를 먼저 열어주세요."); return; }

        var r = _results[i];
        int cx = Math.Max(0, r.X);
        int cy = Math.Max(0, r.Y);
        int cw = Math.Max(1, Math.Min(r.Width, _source.PixelWidth - cx));
        int ch = Math.Max(1, Math.Min(r.Height, _source.PixelHeight - cy));

        var rect = new Int32Rect(cx, cy, cw, ch);
        var cb = new CroppedBitmap(_source, rect);

        // 기존 Crop 창 제거 — 대신 우측 CropPreview에 할당
        CropPreview.Source = cb;

        // 원본 이미지 위에 ROI 그리기
        DrawImageROIOnOverlay(cx, cy, cw, ch);

    }

    private void DrawSelectedROIOnImage(DefectResult r)
    {
        if (_source == null) { ClearImageOverlay(); CropPreview.Source = null; return; }

        int cx = Math.Max(0, r.X);
        int cy = Math.Max(0, r.Y);
        int cw = Math.Max(1, Math.Min(r.Width, _source.PixelWidth - cx));
        int ch = Math.Max(1, Math.Min(r.Height, _source.PixelHeight - cy));

        DrawImageROIOnOverlay(cx, cy, cw, ch);
        // Crop preview 자동 표시
        var rect = new Int32Rect(cx, cy, cw, ch);
        try { CropPreview.Source = new CroppedBitmap(_source, rect); } catch { CropPreview.Source = null; }
    }


    private void ClearImageOverlay()
    {
        ImageOverlayCanvas.Children.Clear();
        CropPreview.Source = null;
    }

    private void DrawImageROIOnOverlay(int imgX, int imgY, int imgW, int imgH)
    {
        // 기존 오버레이 초기화
        ImageOverlayCanvas.Children.Clear();
        if (_source == null) return;

        // 이미지 원본 픽셀 크기
        double imgPixelW = _source.PixelWidth;
        double imgPixelH = _source.PixelHeight;

        // Image 컨트롤의 표시 영역 크기 (Stretch=Uniform 기준)
        double controlW = ImageView.ActualWidth;
        double controlH = ImageView.ActualHeight;
        if (controlW <= 0 || controlH <= 0) return;

        double scale = Math.Min(controlW / imgPixelW, controlH / imgPixelH);
        double dispW = imgPixelW * scale;
        double dispH = imgPixelH * scale;
        double offsetX = (controlW - dispW) / 2.0;
        double offsetY = (controlH - dispH) / 2.0;

        // ROI를 표시할 좌표 변환
        double left = offsetX + imgX * scale;
        double top = offsetY + imgY * scale;
        double width = Math.Max(1.0, imgW * scale);
        double height = Math.Max(1.0, imgH * scale);

        // 화면 밖으로 벗어나면 클램프
        if (left + width < 0 || top + height < 0 || left > controlW || top > controlH) return;

        // ROI 사각형 그리기 (레이블 없음)
        var rect = new Rectangle
        {
            Width = width,
            Height = height,
            Stroke = new SolidColorBrush(Color.FromArgb(220, 220, 20, 20)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(32, 220, 20, 20)),
            RadiusX = 2,
            RadiusY = 2,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect, top);
        ImageOverlayCanvas.Children.Add(rect);
    }

    // JSON 모델
    private class InspectParamsJson
    {
        public double D_FORM_MIN_AREA_RATIO { get; set; }
        public double D_ROUNDNESS { get; set; }
        public double D_DARK_AREA_PERCENT { get; set; }
        public double D_LINEAR_BASE_BRIGHT { get; set; }
        public double D_LINE_ANGLE_LOW { get; set; }
        public double D_LINE_ANGLE_HIGH { get; set; }
        public double D_WHITE_PEAK_IF { get; set; }
        public double D_WHITE_PEAK_ELSEIF { get; set; }
        public double D_WHITE_RATIO { get; set; }
        public double D_WHITE_LINE_PEAK { get; set; }
        public double D_LINEARITY_RATIO { get; set; }
        public int AREA_MIN { get; set; }
        public int NDIL_CNT { get; set; }
    }

    private void SaveParamsToJson()
    {
        var p = new InspectParamsJson
        {
            D_FORM_MIN_AREA_RATIO = _D_FORM_MIN_AREA_RATIO,
            D_ROUNDNESS = _D_ROUNDNESS,
            D_DARK_AREA_PERCENT = _D_DARK_AREA_PERCENT,
            D_LINEAR_BASE_BRIGHT = _D_LINEAR_BASE_BRIGHT,
            D_LINE_ANGLE_LOW = _D_LINE_ANGLE_LOW,
            D_LINE_ANGLE_HIGH = _D_LINE_ANGLE_HIGH,
            D_WHITE_PEAK_IF = _D_WHITE_PEAK_IF,
            D_WHITE_PEAK_ELSEIF = _D_WHITE_PEAK_ELSEIF,
            D_WHITE_RATIO = _D_WHITE_RATIO,
            D_WHITE_LINE_PEAK = _D_WHITE_LINE_PEAK,
            D_LINEARITY_RATIO = _D_LINEARITY_RATIO,
            AREA_MIN = _AREA_MIN,
            NDIL_CNT = _NDIL_CNT
        };

        var opts = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(p, opts);
        File.WriteAllText(_paramsFilePath, json);
    }

    private void LoadParamsFromJson()
    {
        if (!File.Exists(_paramsFilePath)) return;

        string json = File.ReadAllText(_paramsFilePath);
        var p = JsonSerializer.Deserialize<InspectParamsJson>(json);
        if (p == null) return;

        _D_FORM_MIN_AREA_RATIO = p.D_FORM_MIN_AREA_RATIO;
        _D_ROUNDNESS = p.D_ROUNDNESS;
        _D_DARK_AREA_PERCENT = p.D_DARK_AREA_PERCENT;
        _D_LINEAR_BASE_BRIGHT = p.D_LINEAR_BASE_BRIGHT;
        _D_LINE_ANGLE_LOW = p.D_LINE_ANGLE_LOW;
        _D_LINE_ANGLE_HIGH = p.D_LINE_ANGLE_HIGH;
        _D_WHITE_PEAK_IF = p.D_WHITE_PEAK_IF;
        _D_WHITE_PEAK_ELSEIF = p.D_WHITE_PEAK_ELSEIF;
        _D_WHITE_RATIO = p.D_WHITE_RATIO;
        _D_WHITE_LINE_PEAK = p.D_WHITE_LINE_PEAK;
        _D_LINEARITY_RATIO = p.D_LINEARITY_RATIO;
        _AREA_MIN = p.AREA_MIN;
        _NDIL_CNT = p.NDIL_CNT;
    }

    private void DrawLogicDiagramInCanvas(Canvas canvas, DefectResult r)
    {
        if (canvas == null) return;
        canvas.Children.Clear();

        bool isDark = r.IsDark;
        bool isLinear = r.IsLinear;

        bool condFormArea = r.AreaRatio > _D_FORM_MIN_AREA_RATIO;
        bool condRoundness = r.Circularity > _D_ROUNDNESS;
        bool condDarkAreaPct = r.AreaObjPercent > _D_DARK_AREA_PERCENT;
        bool condDarkPeak = r.PeakMax > _D_LINEAR_BASE_BRIGHT;
        bool condAngle = (r.AngleDeg < _D_LINE_ANGLE_LOW) || (r.AngleDeg > _D_LINE_ANGLE_HIGH);

        bool condWVal1_If = r.PeakMax > _D_WHITE_PEAK_IF;
        bool condWVal1_ElseIf = r.PeakMax > _D_WHITE_PEAK_ELSEIF;
        bool condWhiteRatio = r.RatioMopol > _D_WHITE_RATIO;
        bool condWhiteLine = r.PeakMax < _D_WHITE_LINE_PEAK;

        bool inDark = isDark;
        bool inWhite = !isDark;

        // ================= 노드 정의 =================
        var nodes = new List<(int id, string text, double x, double y, double w, double h)> {
        (0,  "흑불량",                                440, 8,   160, 40),
        (1,  "선형성",                                100, 100, 150, 36),
        (3,  "불량 면적비율 > Dark_면적비율", 10,  200, 210, 56),
        (8,  "흑 약불량",                             300, 320, 190, 56),
        (4,  "불량 원형도 > Dark 원형도",           10,  320, 210, 48),
        (5,  "불량 면적% > Dark 면적비율",            10, 430, 230, 48),
        (6,  "분화구",                                10,  530, 130, 32),
        (7,  "크랙",                                  320,  530, 130, 32),

        (2,  "비선형성",                               780, 100, 150, 36),
        (9,  "불량 흑 피크치 > \n Dark 최소밝기", 780, 200, 210, 56),
        (12, "흑 약불량",                            780, 310, 130, 32),
        (10, "Dark 기울기 하한 < \n 불량 기울기 \n < 기울기 상한",         1030, 310, 210, 56),
        (11, "스크래치",                              980, 420, 120, 32),
        (13, "크랙",                                  1120, 420, 120, 32),

        (20, "백불량",                             440, 600, 160, 40),
        (21, "선형성",                               100, 690, 150, 36),
        (22, "White_핀홀 기준값 > 불량 백 피크치",           10,  790, 190, 56),
        (23, "White_돌출 기준값 > 불량 백 피크치",           220, 790, 190, 56),
        (25, "화이트 약불량",                         440, 790, 150, 40),
        (26, "핀홀",                                  10,  900, 120, 32),
        (24, "모폴로지 연산횟수 > \n Ratio<White>",        220, 900, 190, 48),
        (27, "미세긁힘",                              170, 1000, 120, 32),
        (28, "찍힘",                                  320, 1000, 120, 32),

        (29, "비선형성",                                780, 690, 150, 36),
        (30, "White_라인 기준값 < 불량 백 피크치",              780, 800, 190, 56),
        (31, "라인",                                  680, 1000, 120, 32),
        (32, "미세긁힘",                              920, 1000, 120, 32),
};

        var nodeMap = new Dictionary<int, (int id, string text, double x, double y, double w, double h)>();
        foreach (var n in nodes) nodeMap[n.id] = n;

        var resultHighlight = new HashSet<int>();
        switch (r.DefectType)
        {
            case DefectType.Crater: resultHighlight.Add(6); break;
            case DefectType.Crack: resultHighlight.Add(7); break;
            case DefectType.WeakPointD: resultHighlight.Add(8); break;
            case DefectType.Scratch: resultHighlight.Add(11); break;
            case DefectType.Particle: resultHighlight.Add(13); break;
            case DefectType.BlackWeak: resultHighlight.Add(12); break;
            case DefectType.Pinhole: resultHighlight.Add(26); break;
            case DefectType.MicroScratch: resultHighlight.Add(27); break;
            case DefectType.Dent: resultHighlight.Add(28); break;
            case DefectType.WhiteWeak: resultHighlight.Add(25); break;
            case DefectType.Line: resultHighlight.Add(31); break;
            case DefectType.Stain: resultHighlight.Add(32); break;
        }

        var nodeOnPath = new Dictionary<int, bool> {
        { 0,  isDark },
        { 1,  inDark && !isLinear },
        { 2,  inDark && isLinear },
        { 3,  inDark && !isLinear  },
        { 4,  inDark && !isLinear && condFormArea },
        { 5,  inDark && !isLinear && condFormArea && condRoundness },
        { 8,  inDark && !isLinear && !condFormArea},
        { 6,  inDark && !isLinear && condFormArea && condRoundness && condDarkAreaPct },
        { 7,  inDark && !isLinear && condFormArea && (!condRoundness || !condDarkAreaPct) },
        { 9,  inDark && isLinear },
        { 12, inDark && isLinear && !condDarkPeak },
        { 10, inDark && isLinear && condDarkPeak },
        { 11, inDark && isLinear && condDarkPeak && condAngle },
        { 13, inDark && isLinear && condDarkPeak && !condAngle },

        { 20, inWhite },
        { 21, inWhite && !isLinear },
        { 29, inWhite && isLinear },
        { 22, inWhite && !isLinear && condWVal1_If },
        { 26, inWhite && !isLinear && condWVal1_If },
        { 23, inWhite && !isLinear && !condWVal1_If && condWVal1_ElseIf },
        { 24, inWhite && !isLinear && !condWVal1_If && condWVal1_ElseIf },
        { 27, inWhite && !isLinear && !condWVal1_If && condWVal1_ElseIf && condWhiteRatio },
        { 28, inWhite && !isLinear && !condWVal1_If && condWVal1_ElseIf && !condWhiteRatio },
        { 25, inWhite && !isLinear && !condWVal1_If && !condWVal1_ElseIf },
        { 30, inWhite && isLinear },
        { 31, inWhite && isLinear && condWhiteLine },
        { 32, inWhite && isLinear && !condWhiteLine }
    };

        var edgeTaken = new Dictionary<(int from, int to), bool> {
        { (0, 1), isDark && !isLinear },
        { (0, 2), isDark && isLinear  },

        { (1, 3), inDark && !isLinear },
        { (3, 8), inDark && !isLinear && !condFormArea },
        { (3, 4), inDark && !isLinear && condFormArea },

        { (4, 5), inDark && !isLinear && condFormArea && condRoundness },
        { (4, 7), inDark && !isLinear && condFormArea && !condRoundness },

        { (5, 6), inDark && !isLinear && condFormArea && condRoundness && condDarkAreaPct },
        { (5, 7), inDark && !isLinear && condFormArea && condRoundness && !condDarkAreaPct },

        { (2, 9), inDark && isLinear },
        { (9, 12), inDark && isLinear && !condDarkPeak },
        { (9, 10), inDark && isLinear && condDarkPeak },
        { (10, 11), inDark && isLinear && condDarkPeak && condAngle },
        { (10, 13), inDark && isLinear && condDarkPeak && !condAngle },

        { (0, 20), inWhite },
        { (20, 21), inWhite && !isLinear },
        { (20, 29), inWhite && isLinear },

        { (21, 22), inWhite && !isLinear && condWVal1_If },
        { (21, 23), inWhite && !isLinear && !condWVal1_If && condWVal1_ElseIf },
        { (21, 25), inWhite && !isLinear && !condWVal1_If && !condWVal1_ElseIf },

        { (22, 26), inWhite && !isLinear && condWVal1_If },
        { (23, 24), inWhite && !isLinear && !condWVal1_If && condWVal1_ElseIf },
        { (24, 27), inWhite && !isLinear && !condWVal1_If && condWVal1_ElseIf && condWhiteRatio },
        { (24, 28), inWhite && !isLinear && !condWVal1_If && condWVal1_ElseIf && !condWhiteRatio },

        { (29, 30), inWhite && isLinear },
        { (30, 31), inWhite && isLinear && condWhiteLine },
        { (30, 32), inWhite && isLinear && !condWhiteLine },
    };

        var conditionNodeIds = new HashSet<int> { 0, 1, 2, 3, 4, 5, 9, 10, 20, 21, 29, 22, 23, 24 };

        // 간단한 실제 값 툴팁 생성
        string GetActualText(int id)
        {
            return id switch
            {
                3 => $"AreaRatio={r.AreaRatio:F3}",
                4 => $"Circularity={r.Circularity:F3}",
                5 => $"Area%={r.AreaObjPercent:P3}",
                6 => $"Area={r.Area:F0}",
                9 => $"PeakMax={r.PeakMax:F1}",
                10 => $"Angle={r.AngleDeg:F1}",
                22 or 23 => $"PeakMax={r.PeakMax:F1}",
                24 => $"RatioMopol={r.RatioMopol:F3}",
                _ => string.Empty
            };
        }

        // 화살표 그리기 (엘보우) — 색/두께를 경로 채택 여부에 따라 조정
        void DrawElbowArrow(
            (int id, string text, double x, double y, double w, double h) a,
            (int id, string text, double x, double y, double w, double h) b,
            string? label, double exitOffsetX = 0, double midYRatio = 0.5, bool forceGray = false)
        {
            Brush arrowBrush;
            double thickness;
            PenLineCap cap = PenLineCap.Round;

            if (forceGray)
            {
                arrowBrush = Brushes.LightGray;
                thickness = 1.0;
            }
            else
            {
                bool taken = edgeTaken.TryGetValue((a.id, b.id), out var takenVal) && takenVal;
                if (taken) { arrowBrush = Brushes.SeaGreen; thickness = 2.6; }
                else { arrowBrush = Brushes.LightSlateGray; thickness = 1.0; }
            }

            double x1 = a.x + a.w / 2 + exitOffsetX;
            double y1 = a.y + a.h;
            double x2 = b.x + b.w / 2;
            double y2 = b.y;
            double midY = y1 + (y2 - y1) * midYRatio;

            var pts = new[] { new Point(x1, y1), new Point(x1, midY), new Point(x2, midY), new Point(x2, y2 - 8) };
            for (int i = 0; i < pts.Length - 1; i++)
            {
                var line = new Line
                {
                    X1 = pts[i].X,
                    Y1 = pts[i].Y,
                    X2 = pts[i + 1].X,
                    Y2 = pts[i + 1].Y,
                    Stroke = arrowBrush,
                    StrokeThickness = thickness,
                    StrokeStartLineCap = cap,
                    StrokeEndLineCap = cap
                };
                canvas.Children.Add(line);
            }

            // 화살촉: 작게 하고 약간 투명도 적용
            var poly = new Polygon
            {
                Points = new PointCollection { new Point(x2 - 5, y2 - 8), new Point(x2 + 5, y2 - 8), new Point(x2, y2) },
                Fill = arrowBrush,
                Opacity = 0.95
            };
            canvas.Children.Add(poly);

            if (!string.IsNullOrEmpty(label))
            {
                var labelBorder = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = arrowBrush,
                    BorderThickness = new Thickness(0.8),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    Child = new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = arrowBrush }
                };
                Canvas.SetLeft(labelBorder, x2 - 20);
                Canvas.SetTop(labelBorder, midY - 12);
                canvas.Children.Add(labelBorder);
            }
        }

        // 노드 그리기: 조건노드는 true이면 연한 녹색, false이면 연한 빨강(비활성은 WhiteSmoke)
        void DrawNode((int id, string text, double x, double y, double w, double h) node)
        {
            bool isCondition = conditionNodeIds.Contains(node.id);
            bool onPath = nodeOnPath.TryGetValue(node.id, out var onPathVal) && onPathVal;
            bool isResultHighlighted = resultHighlight.Contains(node.id);

            Brush fill;
            Brush border;
            if (isResultHighlighted)
            {
                fill = new LinearGradientBrush(Color.FromRgb(40, 90, 100), Color.FromRgb(60, 120, 130), 90);
                border = Brushes.Black;
            }
            else if (isCondition)
            {
                if (onPath)
                {
                    fill = new LinearGradientBrush(Color.FromRgb(220, 255, 220), Color.FromRgb(190, 240, 190), 90);
                    border = Brushes.SeaGreen;
                }
                else
                {
                    fill = new LinearGradientBrush(Color.FromRgb(245, 245, 245), Color.FromRgb(235, 235, 235), 90);
                    border = Brushes.Gray;
                }
            }
            else
            {
                fill = Brushes.WhiteSmoke;
                border = Brushes.Gray;
            }

            var rect = new Rectangle
            {
                Width = node.w,
                Height = node.h,
                Stroke = border,
                StrokeThickness = isResultHighlighted ? 2.4 : 1.2,
                RadiusX = 6,
                RadiusY = 6,
                Fill = fill
            };

            if (isResultHighlighted)
                rect.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, Opacity = 0.55, ShadowDepth = 3 };

            Canvas.SetLeft(rect, node.x);
            Canvas.SetTop(rect, node.y);
            canvas.Children.Add(rect);

            var tb = new TextBlock
            {
                Text = node.text,
                Width = node.w - 12,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Foreground = isResultHighlighted ? Brushes.White : Brushes.Black,
                FontSize = 11,
                FontWeight = isResultHighlighted ? FontWeights.SemiBold : FontWeights.Normal
            };
            Canvas.SetLeft(tb, node.x + 6);
            Canvas.SetTop(tb, node.y + 6);
            canvas.Children.Add(tb);

            // 툴팁: 중요한 실제 값만 보여줌
            var actual = GetActualText(node.id);
            if (!string.IsNullOrEmpty(actual))
            {
                var tip = new ToolTip { Content = actual };
                ToolTipService.SetToolTip(rect, tip);
                ToolTipService.SetToolTip(tb, tip);
            }
        }

        bool TryNode(int id, out (int id, string text, double x, double y, double w, double h) node) => nodeMap.TryGetValue(id, out node);
        void SafeDraw(int id) { if (TryNode(id, out var n)) DrawNode(n); }
        void SafeArrow(int a, int b, string? label = null, double offset = 0, double midYRatio = 0.5, bool forceGray = false)
        {
            if (TryNode(a, out var na) && TryNode(b, out var nb)) DrawElbowArrow(na, nb, label, offset, midYRatio, forceGray);
        }

        // 노드 먼저 그림
        foreach (var n in nodes) DrawNode(n);

        // ================= 화살표 (라벨 포함) =================
        SafeArrow(0, 1, "FALSE", -60);
        SafeArrow(0, 2, "TRUE", 60);

        SafeArrow(1, 3, "FALSE");
        SafeArrow(3, 8, "FALSE", 80, 0.65);
        SafeArrow(3, 4, "TRUE", 0, 0.5);

        SafeArrow(4, 5, "TRUE", 0, 0.35);
        SafeArrow(4, 7, "FALSE", 80, 0.27);

        SafeArrow(5, 6, "TRUE", 0, 0.35);
        SafeArrow(5, 7, "FALSE", 70, 0.65);

        SafeArrow(2, 9, "TRUE");
        SafeArrow(9, 12, "FALSE");
        SafeArrow(9, 10, "TRUE");
        SafeArrow(10, 11, "TRUE");
        SafeArrow(10, 13, "FALSE");

        SafeArrow(0, 20, null);
        SafeArrow(20, 21, "FALSE", -60);
        SafeArrow(20, 29, "TRUE", 60);
        SafeArrow(21, 22, "If", -60);
        SafeArrow(21, 23, "Else if");
        SafeArrow(21, 25, "Else", 60, 0.25);
        SafeArrow(22, 26, "TRUE");
        SafeArrow(23, 24, "TRUE");
        SafeArrow(24, 27, "TRUE");
        SafeArrow(24, 28, "FALSE");

        SafeArrow(29, 30, "", 0, 0.5);
        SafeArrow(30, 31, "TRUE");
        SafeArrow(30, 32, "FALSE");

        // Legend (좌상단) — 보기 편하게 간단 범례 추가
        void DrawLegend()
        {
            double lx = 12, ly = 12, rectW = 16, rectH = 12, gap = 6;
            var trueRect = new Rectangle { Width = rectW, Height = rectH, Fill = new LinearGradientBrush(Color.FromRgb(220, 255, 220), Color.FromRgb(190, 240, 190), 90), Stroke = Brushes.SeaGreen, StrokeThickness = 1 };
            Canvas.SetLeft(trueRect, lx); Canvas.SetTop(trueRect, ly); canvas.Children.Add(trueRect);
            var trueTxt = new TextBlock { Text = "활성 경로 / TRUE", FontSize = 11 };
            Canvas.SetLeft(trueTxt, lx + rectW + gap); Canvas.SetTop(trueTxt, ly - 2); canvas.Children.Add(trueTxt);

            var falseRect = new Rectangle { Width = rectW, Height = rectH, Fill = new LinearGradientBrush(Color.FromRgb(245, 245, 245), Color.FromRgb(235, 235, 235), 90), Stroke = Brushes.Gray, StrokeThickness = 1 };
            Canvas.SetLeft(falseRect, lx); Canvas.SetTop(falseRect, ly + rectH + gap); canvas.Children.Add(falseRect);
            var falseTxt = new TextBlock { Text = "비활성 / FALSE", FontSize = 11 };
            Canvas.SetLeft(falseTxt, lx + rectW + gap); Canvas.SetTop(falseTxt, ly + rectH + gap - 2); canvas.Children.Add(falseTxt);

            var resRect = new Rectangle { Width = rectW, Height = rectH, Fill = new LinearGradientBrush(Color.FromRgb(45, 75, 85), Color.FromRgb(60, 100, 110), 90), Stroke = Brushes.Black, StrokeThickness = 1 };
            Canvas.SetLeft(resRect, lx); Canvas.SetTop(resRect, ly + 2 * (rectH + gap)); canvas.Children.Add(resRect);
            var resTxt = new TextBlock { Text = "판정 결과 강조", FontSize = 11, Foreground = Brushes.Black };
            Canvas.SetLeft(resTxt, lx + rectW + gap); Canvas.SetTop(resTxt, ly + 2 * (rectH + gap) - 2); canvas.Children.Add(resTxt);
        }

        DrawLegend();

        // 캔버스 크기 재조정
        double maxX = 0, maxY = 0;
        foreach (var n in nodes) { maxX = Math.Max(maxX, n.x + n.w + 20); maxY = Math.Max(maxY, n.y + n.h + 20); }
        canvas.Width = Math.Max(canvas.Width, maxX);
        canvas.Height = Math.Max(canvas.Height, maxY);
    }
}

// 불량 타입 열거형
public enum DefectType
{
    Unknown = 0,
    Scratch = 1,        // 스크래치 (다크 선형)
    Stain = 2,           // 미세긁힘 (백 선형)
    Particle = 3,        // 크랙 (다크 선형)
    Crater = 10,          // 분화구
    Crack = 11,           // 크랙 (다크 비선형)
    WeakPointD = 12,      // 다크 약불량 (WEAK_POINT_D)
    BlackWeak = 13,       // 흑 약불량
    Pinhole = 20,         // 핀홀
    MicroScratch = 21,    // 미세긁힘 (백 비선형)
    Dent = 22,            // 찍힘
    WhiteWeak = 23,       // 화이트 약불량
    Line = 30             // 라인
}

// Managed defect result and native struct
public sealed class DefectResult
{
    public int X, Y, Width, Height;
    public DefectType DefectType;
    public double Area, Mean, AspectRatio;
    public bool IsDark, IsLinear;

    // ---- doc11과 동기화된 실제 판정 특징값 ----
    public double AreaRatio;
    public double Circularity;
    public double AngleDeg;
    public double PeakMax;
    public double AreaObjPercent;
    public double RatioMopol;

    public string DefectTypeName => DefectType switch
    {
        DefectType.Scratch => "SCRATCH",
        DefectType.Crater => "CRATER",
        DefectType.Crack => "CRACK",
        DefectType.WeakPointD => "WEAK_POINT_D",
        DefectType.BlackWeak => "BLACK_WEAK",
        DefectType.Pinhole => "PINHOLE",
        DefectType.MicroScratch => "MICRO_SCRATCH",
        DefectType.Dent => "DENT",
        DefectType.WhiteWeak => "WHITE_WEAK",
        DefectType.Line => "LINE",
        DefectType.Stain => "STAIN",
        DefectType.Particle => "PARTICLE",
        _ => "UNKNOWN"
    };
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct DefectResultNative
{
    public int x, y, width, height;
    public double area, mean, aspectRatio;
    public int defectType;
    public int isDark;
    public int isLinear;

    // ---- doc11과 동기화된 특징값 (C++ 구조체와 순서 동일해야 함) ----
    public double areaRatio;
    public double circularity;
    public double angleDeg;
    public double peakMax;
    public double areaObjPercent;
    public double ratioMopol;

    public DefectResult ToManaged() => new()
    {
        X = x,
        Y = y,
        Width = width,
        Height = height,
        Area = area,
        Mean = mean,
        AspectRatio = aspectRatio,
        DefectType = (DefectType)defectType,
        IsDark = isDark == 1,
        IsLinear = isLinear == 1,

        AreaRatio = areaRatio,
        Circularity = circularity,
        AngleDeg = angleDeg,
        PeakMax = peakMax,
        AreaObjPercent = areaObjPercent,
        RatioMopol = ratioMopol
    };
}