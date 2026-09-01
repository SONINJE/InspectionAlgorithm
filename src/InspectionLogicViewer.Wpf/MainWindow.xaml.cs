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
    private Point _panStart; // 윈도우 좌표계 시작점
    private double _panOriginX, _panOriginY; // 시작 시의 translate 값

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
    private int    _AREA_MIN = 4;
    private int    _NDIL_CNT = 2;

    // JSON 파일 경로
    private readonly string _paramsFilePath = Path.Combine(AppContext.BaseDirectory, "inspect_params.json");

    public MainWindow()
    {
        InitializeComponent();

        // LogicCanvas에 Transform 설정 (Scale -> Translate)
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

        // JSON에서 파라미터 불러오기 (있으면 반영)
        try
        {
            LoadParamsFromJson();
        }
        catch
        {
            // 실패 시 기본값 유지
        }

        // UI에 파라미터 채우기
        FillParameterControlsWithFields();
    }

    private void FillParameterControlsWithFields()
    {
        Param_dark_Ratio.Text = _D_FORM_MIN_AREA_RATIO.ToString("G");
        Param_Roundness.Text = _D_ROUNDNESS.ToString("G");
        Param_DarkAreaPercent.Text = _D_DARK_AREA_PERCENT.ToString("G");
        Param_LinearBaseBright.Text = _D_LINEAR_BASE_BRIGHT.ToString("G");
        Param_LineAngleLow.Text = _D_LINE_ANGLE_LOW.ToString("G");
        Param_LineAngleHigh.Text = _D_LINE_ANGLE_HIGH.ToString("G");
        Param_WhitePeakIf.Text = _D_WHITE_PEAK_IF.ToString("G");
        Param_WhitePeakElseIf.Text = _D_WHITE_PEAK_ELSEIF.ToString("G");
        Param_WhiteRatio.Text = _D_WHITE_RATIO.ToString("G");
        Param_WhiteLinePeak.Text = _D_WHITE_LINE_PEAK.ToString("G");
        Param_LinearityRatio.Text = _D_LINEARITY_RATIO.ToString("G");
        Param_AreaMin.Text = _AREA_MIN.ToString();
        Param_NdilCnt.Text = _NDIL_CNT.ToString();
    }

    private void ApplyFieldsToLocalParams()
    {
        double dv; int iv;
        if (double.TryParse(Param_dark_Ratio.Text, out dv)) _D_FORM_MIN_AREA_RATIO = dv;
        if (double.TryParse(Param_Roundness.Text, out dv)) _D_ROUNDNESS = dv;
        if (double.TryParse(Param_DarkAreaPercent.Text, out dv)) _D_DARK_AREA_PERCENT = dv;
        if (double.TryParse(Param_LinearBaseBright.Text, out dv)) _D_LINEAR_BASE_BRIGHT = dv;
        if (double.TryParse(Param_LineAngleLow.Text, out dv)) _D_LINE_ANGLE_LOW = dv;
        if (double.TryParse(Param_LineAngleHigh.Text, out dv)) _D_LINE_ANGLE_HIGH = dv;
        if (double.TryParse(Param_WhitePeakIf.Text, out dv)) _D_WHITE_PEAK_IF = dv;
        if (double.TryParse(Param_WhitePeakElseIf.Text, out dv)) _D_WHITE_PEAK_ELSEIF = dv;
        if (double.TryParse(Param_WhiteRatio.Text, out dv)) _D_WHITE_RATIO = dv;
        if (double.TryParse(Param_WhiteLinePeak.Text, out dv)) _D_WHITE_LINE_PEAK = dv;
        if (double.TryParse(Param_LinearityRatio.Text, out dv)) _D_LINEARITY_RATIO = dv;
        if (int.TryParse(Param_AreaMin.Text, out iv)) _AREA_MIN = iv;
        if (int.TryParse(Param_NdilCnt.Text, out iv)) _NDIL_CNT = iv;
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

        // 로컬 파라미터를 적용 (UI -> 내부 값)
        ApplyFieldsToLocalParams();

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

        // 추가된 상세 항목
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

        if (_source != null)
        {
            try
            {
                int cx = Math.Max(0, r.X);
                int cy = Math.Max(0, r.Y);
                int cw = Math.Max(1, Math.Min(r.Width, _source.PixelWidth - cx));
                int ch = Math.Max(1, Math.Min(r.Height, _source.PixelHeight - cy));
                var rect = new Int32Rect(cx, cy, cw, ch);
                var cb = new CroppedBitmap(_source, rect);
            }
            catch { }
        }
    }

    private void BuildLogicTree(DefectResult r)
    {
        LogicTreeView.Items.Clear();
        var root = new TreeViewItem { Header = "ROOT", IsExpanded = true, FontWeight = FontWeights.Bold };
        LogicTreeView.Items.Add(root);

        bool isDark = r.IsDark;
        bool isLinear = r.IsLinear;

        var nodeDark = AddCondition(root, "다크성", isDark ? "TRUE" : "FALSE", isDark);
        if (isDark)
        {
            var nodeLine = AddCondition(nodeDark, "라인성", isLinear ? "TRUE" : "FALSE", isLinear);
            if (!isLinear)
            {
                var a = AddCondition(nodeLine, "불량 흑 면적비율 > PARA 형태 판단 최소면", r.AreaRatio, r.AreaRatio > _D_FORM_MIN_AREA_RATIO);
                var b = AddCondition(a, "불량 진원도 > PARA 흑 진원도", r.Circularity, r.Circularity > _D_ROUNDNESS);
                var c = AddCondition(b, "불량 면적% > PARA Dark면적%", r.AreaObjPercent, r.AreaObjPercent > _D_DARK_AREA_PERCENT);
                AddResult(c, "분화구 / 크랙 / WEAK_POINT_D");
            }
            else
            {
                var a = AddCondition(nodeLine, "다크 피크치 > PARA 선형 불량 기준밝기", r.PeakMax, r.PeakMax > _D_LINEAR_BASE_BRIGHT);
                var b = AddCondition(a, "불량각도 < PARA 10도 or >90도", r.AngleDeg, r.AngleDeg < _D_LINE_ANGLE_LOW || r.AngleDeg > _D_LINE_ANGLE_HIGH);
                AddResult(b, "스크래치 / 크랙 / PARA 흑 약불량");
            }
        }
        else
        {
            var nodeWhite = AddCondition(root, "화이트성", "TRUE", true);
            var nodeLineW = AddCondition(nodeWhite, "라인성", isLinear ? "TRUE" : "FALSE", isLinear);

            if (!isLinear)
            {
                var ifNode = AddCondition(nodeLineW, "PARA 불량 PEAK > WHITE_PEAK (IF)", r.PeakMax, r.PeakMax > _D_WHITE_PEAK_IF);
                if (r.PeakMax > _D_WHITE_PEAK_IF) AddResult(ifNode, "핀홀");

                var elifNode = AddCondition(nodeLineW, "PARA 불량 PEAK > WHITE_PEAK (Else if)", r.PeakMax, r.PeakMax > _D_WHITE_PEAK_ELSEIF && r.PeakMax <= _D_WHITE_PEAK_IF);
                if (r.PeakMax > _D_WHITE_PEAK_ELSEIF && r.PeakMax <= _D_WHITE_PEAK_IF)
                {
                    var ratioNode = AddCondition(elifNode, "dRatio_mopol > Ratio<White>", r.RatioMopol, r.RatioMopol > _D_WHITE_RATIO);
                    AddResult(ratioNode, r.RatioMopol > _D_WHITE_RATIO ? "미세긁힘" : "찍힘");
                }

                if (r.PeakMax <= _D_WHITE_PEAK_ELSEIF)
                {
                    var elseNode = AddCondition(nodeLineW, "화이트 약불량 (Else)", r.PeakMax, true);
                    AddResult(elseNode, "화이트 약불량");
                }
            }
            else
            {
                var a = AddCondition(nodeLineW, "Value(라인) < WHITE_PEAK", r.PeakMax, r.PeakMax < _D_WHITE_LINE_PEAK);
                AddResult(a, r.PeakMax < _D_WHITE_LINE_PEAK ? "라인 / 미세긁힘(라인)" : "미세긁힘");
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

    private void ApplyParams_Click(object sender, RoutedEventArgs e)
    {
        // UI -> 내부값 반영, JSON 저장
        ApplyFieldsToLocalParams();
        try
        {
            SaveParamsToJson();
            if (DefectList.SelectedIndex >= 0 && DefectList.SelectedIndex < _results.Count)
            {
                DrawLogicDiagramInCanvas(LogicCanvas, _results[DefectList.SelectedIndex]);
            }
            MessageBox.Show("파라미터를 JSON에 저장했습니다.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파라미터 저장 실패: {ex.Message}");
        }
    }

    private void LoadParamsFromNative_Click(object sender, RoutedEventArgs e)
    {
        // 버튼 명은 그대로 유지했으나 내부는 JSON 로드
        try
        {
            LoadParamsFromJson();
            FillParameterControlsWithFields();
            MessageBox.Show("JSON에서 파라미터를 불러왔습니다.");
            if (DefectList.SelectedIndex >= 0 && DefectList.SelectedIndex < _results.Count)
                DrawLogicDiagramInCanvas(LogicCanvas, _results[DefectList.SelectedIndex]);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파라미터 불러오기 실패: {ex.Message}");
        }
    }

    private void ResetParams_Click(object sender, RoutedEventArgs e)
    {
        // 기본값으로 리셋 (UI 반영, JSON에 저장은 선택적)
        _D_FORM_MIN_AREA_RATIO = 0.25;
        _D_ROUNDNESS = 0.60;
        _D_DARK_AREA_PERCENT = 0.05;
        _D_LINEAR_BASE_BRIGHT = 40.0;
        _D_LINE_ANGLE_LOW = 10.0;
        _D_LINE_ANGLE_HIGH = 90.0;
        _D_WHITE_PEAK_IF = 60.0;
        _D_WHITE_PEAK_ELSEIF = 40.0;
        _D_WHITE_RATIO = 1.50;
        _D_WHITE_LINE_PEAK = 50.0;
        _D_LINEARITY_RATIO = 3.0;
        _AREA_MIN = 4;
        _NDIL_CNT = 2;
        FillParameterControlsWithFields();
    }

    // 다이어그램 그리기 함수는 기존 로직 유지 (필드값 사용)
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
        (0,  "다크성",                                440, 8,   160, 40),
        (1,  "라인성",                                100, 100, 150, 36),
        (3,  "불량 흑 면적비율 > \n PARA 형태 판단 최소면", 10,  200, 210, 56),
        (8,  "흑 약불량",                             300, 320, 190, 56),
        (4,  "불량 진원도 > PARA 흑 진원도",           10,  320, 210, 48),
        (5,  "불량 면적% > PARA Dark면적%",            10, 430, 230, 48),
        (6,  "분화구",                                10,  530, 130, 32),
        (7,  "크랙",                                  320,  530, 130, 32),

        (2,  "라인성",                               780, 100, 150, 36),
        (9,  "다크 피크치 > \n PARA 선형 불량 기준밝기", 780, 200, 210, 56),
        (12, "흑 약불량",                            780, 310, 130, 32),
        (10, "불량각도 < PARA 10도 or >90도",         1030, 310, 210, 48),
        (11, "스크래치",                              980, 420, 120, 32),
        (13, "크랙",                                  1120, 420, 120, 32),

        (20, "화이트성",                             440, 600, 160, 40),
        (21, "라인성",                               100, 690, 150, 36),
        (22, "PARA 핀홀 PEAK > WHITE_PEAK",           10,  790, 190, 56),
        (23, "PARA 돌출 PEAK > WHITE_PEAK",           220, 790, 190, 56),
        (25, "화이트 약불량",                         440, 790, 150, 40),
        (26, "핀홀",                                  10,  900, 120, 32),
        (24, "dRatio_mopol > \n Ratio<White>",        220, 900, 190, 48),
        (27, "미세긁힘",                              170, 1000, 120, 32),
        (28, "찍힘",                                  320, 1000, 120, 32),

        (29, "라인성",                                780, 690, 150, 36),
        (30, "Value(라인) < WHITE_PEAK",              780, 800, 190, 56),
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

        void DrawElbowArrow(
            (int id, string text, double x, double y, double w, double h) a,
            (int id, string text, double x, double y, double w, double h) b,
            string? label, double exitOffsetX = 0, double midYRatio = 0.5, bool forceGray = false)
        {
            Brush arrowBrush; double thickness;

            if (forceGray)
            {
                arrowBrush = Brushes.Gray;
                thickness = 1.2;
            }
            else
            {
                bool taken = edgeTaken.TryGetValue((a.id, b.id), out var takenVal) && takenVal;
                if (taken) { arrowBrush = Brushes.Green; thickness = 2.5; }
                else { arrowBrush = Brushes.Red; thickness = 1.2; }
            }

            double x1 = a.x + a.w / 2 + exitOffsetX;
            double y1 = a.y + a.h;
            double x2 = b.x + b.w / 2;
            double y2 = b.y;
            double midY = y1 + (y2 - y1) * midYRatio;

            var pts = new[] { new Point(x1, y1), new Point(x1, midY), new Point(x2, midY), new Point(x2, y2 - 8) };
            for (int i = 0; i < pts.Length - 1; i++)
                canvas.Children.Add(new Line { X1 = pts[i].X, Y1 = pts[i].Y, X2 = pts[i + 1].X, Y2 = pts[i + 1].Y, Stroke = arrowBrush, StrokeThickness = thickness });

            var poly = new Polygon { Points = new PointCollection { new Point(x2 - 5, y2 - 8), new Point(x2 + 5, y2 - 8), new Point(x2, y2) }, Fill = arrowBrush };
            canvas.Children.Add(poly);

            if (!string.IsNullOrEmpty(label))
            {
                var labelBorder = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = arrowBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(3, 1, 3, 1),
                    Child = new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = arrowBrush }
                };
                Canvas.SetLeft(labelBorder, x2 - 18);
                Canvas.SetTop(labelBorder, midY - 10);
                canvas.Children.Add(labelBorder);
            }
        }

        void DrawNode((int id, string text, double x, double y, double w, double h) node)
        {
            bool isCondition = conditionNodeIds.Contains(node.id);
            bool onPath = nodeOnPath.TryGetValue(node.id, out var onPathVal) && onPathVal;
            bool isResultHighlighted = resultHighlight.Contains(node.id);

            Brush fill = isCondition ? (onPath ? Brushes.LightGreen : Brushes.WhiteSmoke) : (isResultHighlighted ? Brushes.DimGray : Brushes.White);
            Brush border = isCondition ? Brushes.Black : (isResultHighlighted ? Brushes.DarkGreen : Brushes.Gray);

            var rect = new Rectangle { Width = node.w, Height = node.h, Stroke = border, StrokeThickness = isResultHighlighted ? 2 : 1, RadiusX = 5, RadiusY = 5, Fill = fill };
            Canvas.SetLeft(rect, node.x); Canvas.SetTop(rect, node.y);
            canvas.Children.Add(rect);

            var tb = new TextBlock
            {
                Text = node.text,
                Width = node.w - 8,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Foreground = isResultHighlighted ? Brushes.White : Brushes.Black,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Canvas.SetLeft(tb, node.x + 4); Canvas.SetTop(tb, node.y + node.h / 2 - 14);
            canvas.Children.Add(tb);
        }

        bool TryNode(int id, out (int id, string text, double x, double y, double w, double h) node) => nodeMap.TryGetValue(id, out node);
        void SafeDraw(int id) { if (TryNode(id, out var n)) DrawNode(n); }
        void SafeArrow(int a, int b, string? label = null, double offset = 0, double midYRatio = 0.5, bool forceGray = false)
        {
            if (TryNode(a, out var na) && TryNode(b, out var nb)) DrawElbowArrow(na, nb, label, offset, midYRatio, forceGray);
        }

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