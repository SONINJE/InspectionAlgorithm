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
using System.Linq;
using System.Windows.Input;

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

    // 파라미터: MainWindow, ParamDialog, JSON 저장/로드가 모두 이 한 객체(InspectParams)를 공유한다.
    private InspectParams _params = new();

    // 표면 종류: 네이티브 DLL은 이를 구분하지 않으므로 사용자가 검사 대상에 맞게 직접 선택한다.
    // ClassifyDefectType의 실제 판정 로직은 이 값에 따라 완전히 다른 MAP/Tree를 탄다.
    private SurfaceType _surfaceType = SurfaceType.Coating;
    private bool _isInsulGap = false;

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

        try { _params = InspectParams.LoadFromFile(_paramsFilePath); } catch { }
        UpdateParamSummaryPanel();

        // LogicTree 클릭 시 파라미터 하이라이트 처리 핸들러 연결
        LogicTreeView.SelectedItemChanged += LogicTreeView_SelectedItemChanged;

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

    // MAP 패널의 "전체보기": 줌/이동을 초기화하여 다이어그램 전체가 보이도록 맞춘다.
    private void FitView_Click(object sender, RoutedEventArgs e)
    {
        double viewportW = LogicScroll.ActualWidth;
        double viewportH = LogicScroll.ActualHeight;
        if (viewportW <= 0 || viewportH <= 0 || LogicCanvas.Width <= 0 || LogicCanvas.Height <= 0)
        {
            _logicScale.ScaleX = 1.0; _logicScale.ScaleY = 1.0;
            _logicTranslate.X = 0; _logicTranslate.Y = 0;
            return;
        }

        double fitScale = Math.Min(viewportW / LogicCanvas.Width, viewportH / LogicCanvas.Height);
        fitScale = Math.Clamp(fitScale * 0.96, MinZoom, MaxZoom); // 여백을 약간 남김

        _logicScale.ScaleX = fitScale;
        _logicScale.ScaleY = fitScale;
        _logicTranslate.X = (viewportW - LogicCanvas.Width * fitScale) / 2.0;
        _logicTranslate.Y = 10;
    }

    // 표면 종류(코팅부/무지부/절연부) 변경: 실제 판정 로직이 완전히 다른 분기를 타므로
    // MAP/Logic Tree를 다시 그린다. 절연부일 때만 "절연 Gap 영역" 체크박스를 노출한다.
    private void SurfaceTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // XAML의 SelectedIndex="0" 초기값이 InitializeComponent 도중(다른 컨트롤이 아직 연결되기 전)
        // 이 핸들러를 먼저 발생시킬 수 있으므로 방어적으로 가드한다.
        if (InsulGapCheck == null) return;

        _surfaceType = SurfaceTypeCombo.SelectedIndex switch
        {
            1 => SurfaceType.Null,
            2 => SurfaceType.Insul,
            _ => SurfaceType.Coating
        };
        InsulGapCheck.Visibility = _surfaceType == SurfaceType.Insul ? Visibility.Visible : Visibility.Collapsed;
        RefreshDefectListLabels();
    }

    private void InsulGapCheck_Changed(object sender, RoutedEventArgs e)
    {
        _isInsulGap = InsulGapCheck.IsChecked == true;
        RefreshDefectListLabels();
    }

    // Defect List에 표시되는 판정과 Feature 패널의 Type이 항상 "실제 로직" 결과(ComputeRealDefectCode)와
    // 같도록 목록 텍스트를 다시 만든다. 표면 종류/절연 Gap을 바꿀 때마다 호출된다.
    // (Items.Clear()+재선택 과정에서 SelectionChanged가 다시 발생해 Logic Tree/MAP도 함께 갱신된다.)
    private void RefreshDefectListLabels()
    {
        int selected = DefectList.SelectedIndex;
        DefectList.Items.Clear();
        for (int i = 0; i < _results.Count; i++)
        {
            var r = _results[i];
            string code = ComputeRealDefectCode(r);
            DefectList.Items.Add($"#{i + 1}  {RealDefectCatalog.DisplayName(code)} ({code})  ({r.X},{r.Y})  {r.Width} x {r.Height}");
        }
        if (selected >= 0 && selected < DefectList.Items.Count)
            DefectList.SelectedIndex = selected;
    }

    // ── 실제 판정 로직(ClassifyDefectType) 결과 코드 계산 ──────────────
    // BuildXxxLogicTree / DrawXxxDiagram이 그리는 것과 동일한 분기를 따른다.
    // Defect List·Feature 패널에 "Logic Map/Tree가 실제로 어떤 결과를 골랐는지"를 그대로 보여주기 위한 것으로,
    // 이 값이 바뀌면 Tree/MAP 쪽 분기도 반드시 같이 맞춰야 한다.
    private string ComputeRealDefectCode(DefectResult r) => _surfaceType switch
    {
        SurfaceType.Insul => ComputeInsulCode(r),
        SurfaceType.Null => ComputeNullCode(r),
        _ => ComputeCoatingCode(r)
    };

    private string ComputeInsulCode(DefectResult r)
    {
        bool isWhite = !r.IsDark;
        if (!isWhite) return RealDefectCatalog.InsulIsland;

        bool condRatio = r.Ratio > _params.RATIO_W;
        if (_isInsulGap)
            return condRatio ? RealDefectCatalog.InsulGapLine : RealDefectCatalog.InsulGapSpot;
        return condRatio ? RealDefectCatalog.InsulLine : RealDefectCatalog.InsulPinhole;
    }

    private string ComputeNullCode(DefectResult r) =>
        r.Ratio > _params.RATIO_B ? RealDefectCatalog.NoneCoatingWrinkle : RealDefectCatalog.Island;

    private string ComputeCoatingCode(DefectResult r)
    {
        bool isWhite = !r.IsDark;
        double sizeY = r.SizeY;

        if (isWhite)
        {
            bool condLinearW = r.Ratio > _params.RATIO_W && sizeY > _params.SIZEY_W;
            if (condLinearW)
                return r.PeakMax >= _params.TH_WHITE_LINE ? RealDefectCatalog.Line : RealDefectCatalog.ScratchTiny;

            if (r.PeakMax > _params.TH_PINHOLE) return RealDefectCatalog.Pinhole;
            if (r.PeakMax > _params.TH_EXTRUDE)
                return r.RatioMopol < _params.RATIO_W ? RealDefectCatalog.Protrusion : RealDefectCatalog.ScratchTiny;
            return RealDefectCatalog.WeakPointW;
        }
        else
        {
            bool condLinearB = r.Ratio > _params.RATIO_B && sizeY > _params.SIZEY_B;
            if (condLinearB)
            {
                if (r.PeakMax > _params.TH_DARK_DEFECT_MIN)
                    return (r.AngleDeg <= _params.ANGLE_LOW || r.AngleDeg >= _params.ANGLE_HIGH)
                        ? RealDefectCatalog.Scratch : RealDefectCatalog.Crack;
                return RealDefectCatalog.WeakPointD;
            }

            // 원본 로직 그대로: Area<=BlackMinArea면 CRATER의 UseJudge로 게이트된 WEAK_POINT_D.
            if (r.Area <= _params.BLACK_MIN_AREA) return RealDefectCatalog.WeakPointD;
            // Compactness/AreaRatio% 미달로 결과 미지정이던 경로는 CRACK으로 처리(사용자 요청 반영).
            if (r.Circularity <= _params.COMPACTNESS_B) return RealDefectCatalog.Crack;
            return (r.AreaObjPercent * 100.0 > _params.PERCENT_B) ? RealDefectCatalog.Crater : RealDefectCatalog.Crack;
        }
    }

    private void Inspect_Click(object sender, RoutedEventArgs e)
    {
        if (_source == null) { MessageBox.Show("먼저 이미지를 열어주세요."); return; }
        int threshold = (int)_params.THRESHOLD;

        int stride = _source.PixelWidth * 4;
        byte[] pixels = new byte[stride * _source.PixelHeight];
        _source.CopyPixels(pixels, stride, 0);
        var native = new DefectResultNative[256];

        var h = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        int count;
        try
        {
            var treeParams = _params.ToNative();
            SetRealTreeParams(ref treeParams);

            count = InspectImage(h.AddrOfPinnedObject(), _source.PixelWidth, _source.PixelHeight,
                stride, threshold, (int)_surfaceType, _isInsulGap ? 1 : 0, native, native.Length);
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

        _results.Clear();
        for (int i = 0; i < count; i++)
            _results.Add(native[i].ToManaged());

        RefreshDefectListLabels();
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
        Fratio.Text = r.Ratio.ToString("F2");
        Fsize.Text = $"{r.SizeX:F2} x {r.SizeY:F2}";

        string realCode = ComputeRealDefectCode(r);
        Ftype.Text = RealDefectCatalog.DisplayName(realCode);
        FDefectCode.Text = realCode;
        FIsDark.Text = r.IsDark ? "TRUE" : "FALSE";
        FIsLinear.Text = r.IsLinear ? "TRUE" : "FALSE";

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
        if (abs < MarginDangerPercent) return (new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)), "⚠ ");
        if (abs < MarginCautionPercent) return (new SolidColorBrush(Color.FromRgb(0xC9, 0x8A, 0x1F)), "△ ");
        return (new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x5B)), "");
    }

    // 여유값 텍스트 한 줄 생성 (공통)
    private static TextBlock BuildMarginLine(double margin, double marginPercent)
    {
        var (color, icon) = GetMarginStyle(marginPercent);
        string sign = margin >= 0 ? "+" : "";
        return new TextBlock
        {
            Text = $"{icon}여유: {sign}{margin:F2}  ({sign}{marginPercent:F1}%)",
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
            FontWeight = Math.Abs(marginPercent) < MarginCautionPercent ? FontWeights.Bold : FontWeights.Normal,
            Foreground = color
        };
    }

    // TRUE/FALSE 배지 (작은 알약 모양)
    private static Border BuildResultBadge(bool result)
    {
        var c = result ? Color.FromRgb(0x2E, 0x9E, 0x5B) : Color.FromRgb(0x8B, 0x8F, 0x9E);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(28, c.R, c.G, c.B)),
            BorderBrush = new SolidColorBrush(c),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 1, 8, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = result ? "TRUE" : "FALSE",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(c)
            }
        };
    }

    // 조건/비교 노드를 감싸는 카드: 왼쪽에 결과 색 강조 바, 옅은 배경, 둥근 모서리
    private static Border BuildTreeCard(bool result, UIElement content)
    {
        Color accent = result ? Color.FromRgb(0x2E, 0x9E, 0x5B) : Color.FromRgb(0xC0, 0xC4, 0xD4);
        Color bg = result ? Color.FromRgb(0xF0, 0xFA, 0xF3) : Color.FromRgb(0xFA, 0xFB, 0xFD);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bar = new Border { Background = new SolidColorBrush(accent) };
        Grid.SetColumn(bar, 0);

        var body = new Border { Padding = new Thickness(10, 8, 10, 8), Child = content };
        Grid.SetColumn(body, 1);

        grid.Children.Add(bar);
        grid.Children.Add(body);

        return new Border
        {
            Background = new SolidColorBrush(bg),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE7, 0xE9, 0xF1)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 3, 8, 3),
            ClipToBounds = true,
            Child = grid
        };
    }

    // 제목 + TRUE/FALSE 배지를 한 줄에 배치
    private static DockPanel BuildTitleRow(string title, bool result)
    {
        var row = new DockPanel { LastChildFill = true };
        var badge = BuildResultBadge(result);
        DockPanel.SetDock(badge, Dock.Right);
        row.Children.Add(badge);
        row.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 8, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x26, 0x2F))
        });
        return row;
    }

    private static TextBlock BuildDetailLine(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Margin = new Thickness(0, 4, 0, 0),
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.FromRgb(0x75, 0x79, 0x8A))
    };

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
        panel.Children.Add(BuildTitleRow(title, result));
        panel.Children.Add(BuildDetailLine($"{actualName} = {actualValue.ToString(format)}   {op}   {paramName} = {paramValue.ToString(format)}"));

        var (margin, marginPercent) = ComputeMargin(actualValue, paramValue, op);
        panel.Children.Add(BuildMarginLine(margin, marginPercent));

        // LogicTree 선택 시 연결할 파라미터 키
        var item = new TreeViewItem
        {
            Header = BuildTreeCard(result, panel),
            IsExpanded = true,
            Tag = new[] { paramName }
        };
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
        panel.Children.Add(BuildTitleRow(title, result));
        panel.Children.Add(BuildDetailLine(
            $"{actualName} = {actualValue.ToString(format)}   " +
            $"(< {lowName}={lowParam.ToString(format)}  또는  > {highName}={highParam.ToString(format)})"));

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

        // 범위 조건은 하한/상한 파라미터를 함께 강조
        var item = new TreeViewItem
        {
            Header = BuildTreeCard(result, panel),
            IsExpanded = true,
            Tag = new[] { lowName, highName }
        };
        parent.Items.Add(item);
        return item;
    }

    // LogicTree의 비교 노드를 클릭하면 하단 파라미터 요약에서 사용한 값을 강조한다.
    private void LogicTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        ClearParamHighlights();

        if (e.NewValue is not TreeViewItem { Tag: string[] parameterKeys })
            return;

        foreach (string parameterKey in parameterKeys)
        {
            if (GetParamValueTextBlock(parameterKey) is not TextBlock parameterText)
                continue;

            parameterText.Background = Brushes.Gold;
            parameterText.Foreground = Brushes.Black;
            parameterText.FontWeight = FontWeights.Bold;
            parameterText.BringIntoView();
        }
    }

    private void ClearParamHighlights()
    {
        foreach (var tb in ParamSummaryGrid.Children.OfType<TextBlock>())
        {
            if (tb.Tag is not string) continue; // 라벨 TextBlock(Tag 없음)은 건너뜀

            // 동적 생성 시 부여한 기본 스타일로 복원한다.
            tb.ClearValue(TextBlock.BackgroundProperty);
            tb.Foreground = Brushes.Black;
            tb.FontWeight = FontWeights.SemiBold;
        }
    }

    private TextBlock? GetParamValueTextBlock(string parameterKey) =>
        ParamSummaryGrid.Children.OfType<TextBlock>().FirstOrDefault(tb => (tb.Tag as string) == parameterKey);

    // ClassifyDefectType의 실제 판정 사용 여부(UseJudge)를 흉내 낸다. 꺼져 있으면 결과는 UNKNOWN으로 남는다.
    private bool Judged(string code) => !_params.UseJudge.TryGetValue(code, out var v) || v;

    // 판정 코드를 최종 결과 카드로 표시한다. UseJudge가 꺼져 있으면 UNKNOWN 카드로 대체한다.
    private void AddClassifiedResult(TreeViewItem parent, string code)
    {
        if (Judged(code))
            AddResult(parent, $"{RealDefectCatalog.DisplayName(code)} ({code})");
        else
            AddUnknownResult(parent, $"UseJudge(\"{code}\")가 꺼져 있어 UNKNOWN으로 남습니다.");
    }

    private static void AddUnknownResult(TreeViewItem parent, string reason)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = "UNKNOWN (판정 불가)",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x26, 0x2F))
        });
        body.Children.Add(new TextBlock
        {
            Text = reason,
            FontSize = 11.5,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x75, 0x79, 0x8A))
        });

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF2, 0xF7)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC7, 0xCB, 0xDA)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 3, 8, 3),
            Child = body
        };
        parent.Items.Add(new TreeViewItem { Header = card });
    }

    private void BuildLogicTree(DefectResult r)
    {
        LogicTreeView.Items.Clear();
        var root = new TreeViewItem { Header = "ROOT", IsExpanded = true, FontWeight = FontWeights.Bold };
        LogicTreeView.Items.Add(root);

        switch (_surfaceType)
        {
            case SurfaceType.Insul: BuildInsulLogicTree(root, r); break;
            case SurfaceType.Null: BuildNullLogicTree(root, r); break;
            default: BuildCoatingLogicTree(root, r); break;
        }

        DrawLogicDiagramInCanvas(LogicCanvas, r);
    }

    // ── 절연부(ELEC_INSUL) ── IsWhite → (Gap 여부) → Ratio_W 비교
    private void BuildInsulLogicTree(TreeViewItem root, DefectResult r)
    {
        bool isWhite = !r.IsDark;
        var nodeWhite = AddCondition(root, "IsWhite (백불량 여부)", isWhite);

        if (isWhite)
        {
            var nodeGap = AddCondition(nodeWhite, "절연 Gap 영역 여부 (수동 설정)", _isInsulGap);

            if (_isInsulGap)
            {
                bool condRatio = r.Ratio > _params.RATIO_W;
                var a = AddParamCompare(nodeGap, "Gap 라인성: Ratio가 Ratio_W보다 큰가",
                    r.Ratio, "Ratio", ">", _params.RATIO_W, "RATIO_W", condRatio, "F2");
                AddClassifiedResult(a, condRatio ? RealDefectCatalog.InsulGapLine : RealDefectCatalog.InsulGapSpot);
            }
            else
            {
                bool condRatio = r.Ratio > _params.RATIO_W;
                var a = AddParamCompare(nodeGap, "라인성: Ratio가 Ratio_W보다 큰가",
                    r.Ratio, "Ratio", ">", _params.RATIO_W, "RATIO_W", condRatio, "F2");
                AddClassifiedResult(a, condRatio ? RealDefectCatalog.InsulLine : RealDefectCatalog.InsulPinhole);
            }
        }
        else
        {
            AddClassifiedResult(nodeWhite, RealDefectCatalog.InsulIsland);
        }
    }

    // ── 무지부(ELEC_NULL) ── 흑불량만 존재. Ratio_B 초과면 주름, 그 외엔 Compactness_B와 무관하게 아일랜드.
    private void BuildNullLogicTree(TreeViewItem root, DefectResult r)
    {
        bool condRatio = r.Ratio > _params.RATIO_B;
        var a = AddParamCompare(root, "Ratio가 Ratio_B보다 큰가",
            r.Ratio, "Ratio", ">", _params.RATIO_B, "RATIO_B", condRatio, "F2");

        if (condRatio)
        {
            AddClassifiedResult(a, RealDefectCatalog.NoneCoatingWrinkle);
        }
        else
        {
            bool condCompact = r.Circularity > _params.COMPACTNESS_B;
            var b = AddParamCompare(a, "Compactness가 Compactness_B보다 큰가 (참고용 — 두 경로 모두 결과 동일)",
                r.Circularity, "Compactness", ">", _params.COMPACTNESS_B, "COMPACTNESS_B", condCompact, "F2");
            AddClassifiedResult(b, RealDefectCatalog.Island);
        }
    }

    // ── 코팅부 ── 백/흑 각각 실제 ClassifyDefectType 코팅부 분기를 그대로 따른다.
    private void BuildCoatingLogicTree(TreeViewItem root, DefectResult r)
    {
        bool isWhite = !r.IsDark;
        double sizeX = r.SizeX;
        double sizeY = r.SizeY;

        var nodeWB = AddCondition(root, "IsWhite (백불량 여부)", isWhite);

        if (isWhite)
        {
            bool condRatio = r.Ratio > _params.RATIO_W;
            bool condSizeY = sizeY > _params.SIZEY_W;
            bool condLinear = condRatio && condSizeY;
            var linNode = new TreeViewItem
            {
                Header = BuildTreeCard(condLinear, BuildAndConditionPanel(
                    "라인성 판단 (AND 조건)",
                    ("Ratio", r.Ratio, "RATIO_W", _params.RATIO_W, "F2", condRatio),
                    ("SizeY", sizeY, "SIZEY_W", _params.SIZEY_W, "F2", condSizeY))),
                IsExpanded = true,
                Tag = new[] { "RATIO_W", "SIZEY_W" }
            };
            nodeWB.Items.Add(linNode);

            if (condLinear)
            {
                bool condPeakLine = r.PeakMax >= _params.TH_WHITE_LINE;
                var p = AddParamCompare(linNode, "PeakValue가 Th_WhiteLine 이상인가",
                    r.PeakMax, "PeakValue", ">=", _params.TH_WHITE_LINE, "TH_WHITE_LINE", condPeakLine, "F1");
                AddClassifiedResult(p, condPeakLine ? RealDefectCatalog.Line : RealDefectCatalog.ScratchTiny);
            }
            else
            {
                bool condPinhole = r.PeakMax > _params.TH_PINHOLE;
                var p1 = AddParamCompare(linNode, "PeakValue가 Th_Pinhole보다 큰가",
                    r.PeakMax, "PeakValue", ">", _params.TH_PINHOLE, "TH_PINHOLE", condPinhole, "F1");

                if (condPinhole)
                {
                    AddClassifiedResult(p1, RealDefectCatalog.Pinhole);
                }
                else
                {
                    bool condExtrude = r.PeakMax > _params.TH_EXTRUDE;
                    var p2 = AddParamCompare(p1, "PeakValue가 Th_Extrude보다 큰가",
                        r.PeakMax, "PeakValue", ">", _params.TH_EXTRUDE, "TH_EXTRUDE", condExtrude, "F1");

                    if (condExtrude)
                    {
                        bool condMopol = r.RatioMopol < _params.RATIO_W;
                        var p3 = AddParamCompare(p2, "MopologyRatio가 Ratio_W보다 작은가",
                            r.RatioMopol, "MopologyRatio", "<", _params.RATIO_W, "RATIO_W", condMopol, "F2");
                        AddClassifiedResult(p3, condMopol ? RealDefectCatalog.Protrusion : RealDefectCatalog.ScratchTiny);
                    }
                    else
                    {
                        AddClassifiedResult(p2, RealDefectCatalog.WeakPointW);
                    }
                }
            }
        }
        else
        {
            bool condRatio = r.Ratio > _params.RATIO_B;
            bool condSizeY = sizeY > _params.SIZEY_B;
            bool condLinear = condRatio && condSizeY;

            var linNode = new TreeViewItem
            {
                Header = BuildTreeCard(condLinear, BuildAndConditionPanel(
                    "라인성 판단 (AND 조건)",
                    ("Ratio", r.Ratio, "RATIO_B", _params.RATIO_B, "F2", condRatio),
                    ("SizeY", sizeY, "SIZEY_B", _params.SIZEY_B, "F2", condSizeY))),
                IsExpanded = true,
                Tag = new[] { "RATIO_B", "SIZEY_B" }
            };
            nodeWB.Items.Add(linNode);

            if (condLinear)
            {
                // 참고: 원본 코드는 SizeX > SizeX_B 이면 resultCode = UNKNOWN을 대입하지만
                // 바로 다음 if 문이 조건 없이 다시 덮어써서 실제 결과에는 영향을 주지 않는다(원본의 사실상 죽은 코드).
                bool condSizeX = sizeX > _params.SIZEX_B;
                if (condSizeX)
                    AddInfoLine(linNode, $"참고: SizeX({sizeX:F2}) > SIZEX_B({_params.SIZEX_B:F2}) — 원본 로직에서 이 비교는 결과에 영향을 주지 않습니다.");

                bool condPeak = r.PeakMax > _params.TH_DARK_DEFECT_MIN;
                var p1 = AddParamCompare(linNode, "PeakValue가 Th_DarkDefectMin보다 큰가",
                    r.PeakMax, "PeakValue", ">", _params.TH_DARK_DEFECT_MIN, "TH_DARK_DEFECT_MIN", condPeak, "F1");

                if (condPeak)
                {
                    bool condAngle = r.AngleDeg <= _params.ANGLE_LOW || r.AngleDeg >= _params.ANGLE_HIGH;
                    var p2 = AddRangeCompare(p1, "각도: 하한 이하 또는 상한 이상이면 스크래치",
                        r.AngleDeg, "Angle", _params.ANGLE_LOW, "ANGLE_LOW", _params.ANGLE_HIGH, "ANGLE_HIGH", condAngle);
                    AddClassifiedResult(p2, condAngle ? RealDefectCatalog.Scratch : RealDefectCatalog.Crack);
                }
                else
                {
                    AddClassifiedResult(p1, RealDefectCatalog.WeakPointD);
                }
            }
            else
            {
                bool condArea = r.Area > _params.BLACK_MIN_AREA;
                var a = AddParamCompare(linNode, "Area가 BlackMinArea보다 큰가",
                    r.Area, "Area", ">", _params.BLACK_MIN_AREA, "BLACK_MIN_AREA", condArea, "F0");

                if (condArea)
                {
                    bool condCompact = r.Circularity > _params.COMPACTNESS_B;
                    var b = AddParamCompare(a, "Compactness가 Compactness_B보다 큰가",
                        r.Circularity, "Compactness", ">", _params.COMPACTNESS_B, "COMPACTNESS_B", condCompact, "F2");

                    if (condCompact)
                    {
                        bool condPercent = r.AreaObjPercent * 100.0 > _params.PERCENT_B;
                        var c = AddParamCompare(b, "AreaRatioWithinRoi(%)가 Percent_B보다 큰가",
                            r.AreaObjPercent * 100.0, "AreaRatioWithinRoi%", ">", _params.PERCENT_B, "PERCENT_B", condPercent, "F2");

                        if (condPercent)
                            AddClassifiedResult(c, RealDefectCatalog.Crater);
                        else
                            AddClassifiedResult(c, RealDefectCatalog.Crack);
                    }
                    else
                    {
                        AddClassifiedResult(b, RealDefectCatalog.Crack);
                    }
                }
                else
                {
                    // 원본 코드는 UseJudge(CRATER)로 게이트하면서 실제로는 WEAK_POINT_D를 대입한다 (원본의 네이밍 불일치로 보이는 부분).
                    if (Judged(RealDefectCatalog.Crater))
                        AddResult(a, $"{RealDefectCatalog.DisplayName(RealDefectCatalog.WeakPointD)} ({RealDefectCatalog.WeakPointD}) — 원본 로직에서 CRATER의 UseJudge로 게이트됨");
                    else
                        AddUnknownResult(a, $"UseJudge(\"{RealDefectCatalog.Crater}\")가 꺼져 있어 UNKNOWN으로 남습니다. (원본 로직에서 이 경로는 CRATER의 UseJudge로 게이트됨)");
                }
            }
        }
    }

    // AND 조건(라인성 판단)을 한 카드 안에 보기 좋게 나열한다.
    private static StackPanel BuildAndConditionPanel(string title, params (string name, double actual, string paramName, double paramValue, string format, bool result)[] rows)
    {
        var panel = new StackPanel();
        panel.Children.Add(BuildTitleRow(title, rows.All(x => x.result)));
        foreach (var row in rows)
        {
            panel.Children.Add(BuildDetailLine($"{row.name} = {row.actual.ToString(row.format)}   {(row.result ? "✓" : "✗")}   {row.paramName} = {row.paramValue.ToString(row.format)}"));
        }
        return panel;
    }

    private static void AddInfoLine(TreeViewItem parent, string text)
    {
        var tb = new TextBlock
        {
            Text = "ℹ " + text,
            FontSize = 11,
            Margin = new Thickness(4, 2, 8, 6),
            TextWrapping = TextWrapping.Wrap,
            FontStyle = FontStyles.Italic,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA2, 0xA6, 0xB5))
        };
        parent.Items.Add(new TreeViewItem { Header = tb });
    }

    private static TreeViewItem AddCondition(TreeViewItem parent, string text, bool result)
    {
        var titleRow = BuildTitleRow(text, result);
        titleRow.Children.OfType<TextBlock>().First().FontSize = 14;

        var item = new TreeViewItem { Header = BuildTreeCard(result, titleRow), IsExpanded = true };
        parent.Items.Add(item);
        return item;
    }

    // 최종 판정 결과: MAP의 판정 결과 강조색(액센트)과 통일해 한눈에 눈에 띄게 표시
    private static void AddResult(TreeViewItem parent, string text)
    {
        var body = new TextBlock
        {
            Text = "▶  " + text,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        };

        var card = new Border
        {
            Background = new LinearGradientBrush(Color.FromRgb(0x5B, 0x6E, 0xF5), Color.FromRgb(0x47, 0x56, 0xD6), 90),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 3, 8, 3),
            Child = body
        };

        parent.Items.Add(new TreeViewItem { Header = card });
    }

    [DllImport("InspectionAlgorithm.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int InspectImage(IntPtr image, int width, int height, int stride, int threshold,
        int surfaceType, int isInsulGap,
        [Out] DefectResultNative[] results, int maxResults);

    [DllImport("InspectionAlgorithm.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetRealTreeParams(ref RealTreeParamsNative p);

    // 하단 파라미터 요약 패널 갱신: ParamCatalog를 기반으로 라벨/값 TextBlock 쌍을 동적 생성한다.
    // 값 TextBlock의 Tag에 파라미터 Key를 심어두어 LogicTree 클릭 시 하이라이트 매칭에 사용한다.
    private void UpdateParamSummaryPanel()
    {
        ParamSummaryGrid.Children.Clear();

        foreach (var field in ParamCatalog.Fields)
        {
            ParamSummaryGrid.Children.Add(new TextBlock
            {
                Text = field.Label,
                FontSize = 11,
                Margin = new Thickness(2),
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            });

            ParamSummaryGrid.Children.Add(new TextBlock
            {
                Text = field.DisplayValue(_params),
                FontSize = 11,
                Margin = new Thickness(2),
                FontWeight = FontWeights.SemiBold,
                Tag = field.Key
            });
        }
    }

    // 파라미터 다이얼로그 열기
    private void OpenParamsDialog_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ParamDialog(_params) { Owner = this };

        if (dlg.ShowDialog() == true)
        {
            _params = dlg.Params;

            try
            {
                _params.SaveToFile(_paramsFilePath);
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

    // ── MAP 공용 인프라 ───────────────────────────────────────────
    // 표면 종류별로 노드 그래프만 다르고, 그리는 방식(카드 스타일/화살표 스타일/범례/캔버스 크기 조정)은
    // 공용으로 재사용한다. id를 문자열로 두어 표면 종류마다 독립적인 그래프를 자유롭게 정의할 수 있다.
    private readonly struct MapNode
    {
        public readonly string Id;
        public readonly string Text;
        public readonly double X, Y, W, H;
        public MapNode(string id, string text, double x, double y, double w, double h)
        { Id = id; Text = text; X = x; Y = y; W = w; H = h; }
    }

    private void DrawMapNode(Canvas canvas, MapNode node, bool isCondition, bool onPath, bool isResultHighlighted, string? tooltip)
    {
        Brush fill;
        Brush borderBrush;
        if (isResultHighlighted)
        {
            fill = new LinearGradientBrush(Color.FromRgb(0x5B, 0x6E, 0xF5), Color.FromRgb(0x47, 0x56, 0xD6), 90);
            borderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x3F, 0xB8));
        }
        else if (isCondition)
        {
            if (onPath)
            {
                fill = new LinearGradientBrush(Color.FromRgb(0xE1, 0xF7, 0xE9), Color.FromRgb(0xCC, 0xF0, 0xDB), 90);
                borderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x5B));
            }
            else
            {
                fill = new LinearGradientBrush(Color.FromRgb(0xFA, 0xFB, 0xFD), Color.FromRgb(0xF1, 0xF2, 0xF7), 90);
                borderBrush = new SolidColorBrush(Color.FromRgb(0xB7, 0xBB, 0xC9));
            }
        }
        else
        {
            fill = new SolidColorBrush(Color.FromRgb(0xFA, 0xFB, 0xFD));
            borderBrush = new SolidColorBrush(Color.FromRgb(0xB7, 0xBB, 0xC9));
        }

        // Rectangle+TextBlock 대신 Border 하나로 그려서 텍스트가 박스 안에 상하좌우 정중앙으로 오도록 한다.
        var tb = new TextBlock
        {
            Text = node.Text,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = isResultHighlighted ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x22, 0x26, 0x2F)),
            FontSize = 11.5,
            LineHeight = 15,
            FontWeight = isResultHighlighted ? FontWeights.SemiBold : FontWeights.Medium
        };

        var card = new Border
        {
            Width = node.W,
            Height = node.H,
            Background = fill,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(isResultHighlighted ? 2.2 : 1.3),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 4, 8, 4),
            Child = tb
        };

        if (isResultHighlighted)
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, Opacity = 0.45, ShadowDepth = 3 };

        Canvas.SetLeft(card, node.X);
        Canvas.SetTop(card, node.Y);
        canvas.Children.Add(card);

        if (!string.IsNullOrEmpty(tooltip))
            ToolTipService.SetToolTip(card, new ToolTip { Content = tooltip });
    }

    // 화살표 그리기 (엘보우) — 색/두께를 경로 채택 여부에 따라 조정
    private void DrawMapArrow(Canvas canvas, MapNode a, MapNode b, string? label, bool taken,
        double exitOffsetX = 0, double midYRatio = 0.5, bool forceGray = false)
    {
        Brush arrowBrush;
        double thickness;
        const PenLineCap cap = PenLineCap.Round;

        if (forceGray)
        {
            arrowBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0xD9, 0xE6));
            thickness = 1.0;
        }
        else if (taken)
        {
            arrowBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x5B));
            thickness = 2.4;
        }
        else
        {
            arrowBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC4, 0xD4));
            thickness = 1.2;
        }

        double x1 = a.X + a.W / 2 + exitOffsetX;
        double y1 = a.Y + a.H;
        double x2 = b.X + b.W / 2;
        double y2 = b.Y;
        double midY = y1 + (y2 - y1) * midYRatio;

        // 꺾이는 지점을 둥글게 이어 부드러운 흐름선으로 보이게 한다 (Polyline + Round join).
        var pts = new[] { new Point(x1, y1), new Point(x1, midY), new Point(x2, midY), new Point(x2, y2 - 8) };
        var polyline = new Polyline
        {
            Points = new PointCollection(pts),
            Stroke = arrowBrush,
            StrokeThickness = thickness,
            StrokeStartLineCap = cap,
            StrokeEndLineCap = cap,
            StrokeLineJoin = PenLineJoin.Round
        };
        canvas.Children.Add(polyline);

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
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = arrowBrush }
            };
            Canvas.SetLeft(labelBorder, x2 - 20);
            Canvas.SetTop(labelBorder, midY - 12);
            canvas.Children.Add(labelBorder);
        }
    }

    private static void DrawMapLegend(Canvas canvas)
    {
        double lx = 12, ly = 12, rectW = 16, rectH = 12, gap = 6;
        var textColor = new SolidColorBrush(Color.FromRgb(0x22, 0x26, 0x2F));

        var trueRect = new Rectangle { Width = rectW, Height = rectH, RadiusX = 3, RadiusY = 3, Fill = new LinearGradientBrush(Color.FromRgb(0xE1, 0xF7, 0xE9), Color.FromRgb(0xCC, 0xF0, 0xDB), 90), Stroke = new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x5B)), StrokeThickness = 1 };
        Canvas.SetLeft(trueRect, lx); Canvas.SetTop(trueRect, ly); canvas.Children.Add(trueRect);
        var trueTxt = new TextBlock { Text = "활성 경로 / TRUE", FontSize = 11, Foreground = textColor };
        Canvas.SetLeft(trueTxt, lx + rectW + gap); Canvas.SetTop(trueTxt, ly - 2); canvas.Children.Add(trueTxt);

        var falseRect = new Rectangle { Width = rectW, Height = rectH, RadiusX = 3, RadiusY = 3, Fill = new LinearGradientBrush(Color.FromRgb(0xFA, 0xFB, 0xFD), Color.FromRgb(0xF1, 0xF2, 0xF7), 90), Stroke = new SolidColorBrush(Color.FromRgb(0xB7, 0xBB, 0xC9)), StrokeThickness = 1 };
        Canvas.SetLeft(falseRect, lx); Canvas.SetTop(falseRect, ly + rectH + gap); canvas.Children.Add(falseRect);
        var falseTxt = new TextBlock { Text = "비활성 / FALSE", FontSize = 11, Foreground = textColor };
        Canvas.SetLeft(falseTxt, lx + rectW + gap); Canvas.SetTop(falseTxt, ly + rectH + gap - 2); canvas.Children.Add(falseTxt);

        var resRect = new Rectangle { Width = rectW, Height = rectH, RadiusX = 3, RadiusY = 3, Fill = new LinearGradientBrush(Color.FromRgb(0x5B, 0x6E, 0xF5), Color.FromRgb(0x47, 0x56, 0xD6), 90), Stroke = new SolidColorBrush(Color.FromRgb(0x33, 0x3F, 0xB8)), StrokeThickness = 1 };
        Canvas.SetLeft(resRect, lx); Canvas.SetTop(resRect, ly + 2 * (rectH + gap)); canvas.Children.Add(resRect);
        var resTxt = new TextBlock { Text = "판정 결과 강조", FontSize = 11, Foreground = textColor };
        Canvas.SetLeft(resTxt, lx + rectW + gap); Canvas.SetTop(resTxt, ly + 2 * (rectH + gap) - 2); canvas.Children.Add(resTxt);
    }

    private static void FinalizeCanvasSize(Canvas canvas, IEnumerable<MapNode> nodes)
    {
        double maxX = 960, maxY = 600;
        foreach (var n in nodes) { maxX = Math.Max(maxX, n.X + n.W + 20); maxY = Math.Max(maxY, n.Y + n.H + 20); }
        canvas.Width = maxX;
        canvas.Height = maxY;
    }

    private void DrawLogicDiagramInCanvas(Canvas canvas, DefectResult r)
    {
        if (canvas == null) return;
        canvas.Children.Clear();

        switch (_surfaceType)
        {
            case SurfaceType.Insul: DrawInsulDiagram(canvas, r); break;
            case SurfaceType.Null: DrawNullDiagram(canvas, r); break;
            default: DrawCoatingDiagram(canvas, r); break;
        }

        DrawMapLegend(canvas);
    }

    // ── 절연부(ELEC_INSUL) MAP ──
    private void DrawInsulDiagram(Canvas canvas, DefectResult r)
    {
        bool isWhite = !r.IsDark;
        bool condRatioGap = r.Ratio > _params.RATIO_W;
        bool condRatioNotGap = r.Ratio > _params.RATIO_W;

        var nodes = new List<MapNode>
        {
            new("root", "IsWhite ?\n(절연부)", 420, 8, 180, 48),
            new("gap",  "절연 Gap 영역?\n(수동 설정)", 150, 120, 220, 56),
            new("notgap_cond", "Ratio > Ratio_W ?", 480, 230, 220, 56),
            new("gap_cond", "Ratio > Ratio_W ?", 60, 230, 220, 56),
            new("r_gapline", "INSUL_GAP_LINE\n(절연 Gap 라인)", 20, 340, 190, 48),
            new("r_gapspot", "INSUL_GAP_SPOT\n(절연 Gap 점불량)", 230, 340, 190, 48),
            new("r_line", "INSUL_LINE\n(절연 라인)", 440, 340, 190, 48),
            new("r_pinhole", "INSUL_PINHOLE\n(절연 핀홀)", 650, 340, 190, 48),
            new("r_island", "INSUL_ISLAND\n(절연 아일랜드)", 760, 120, 200, 48),
        };
        var map = nodes.ToDictionary(n => n.Id);

        bool onGapPath = isWhite && _isInsulGap;
        bool onNotGapPath = isWhite && !_isInsulGap;

        var conditionIds = new HashSet<string> { "root", "gap", "gap_cond", "notgap_cond" };
        var onPath = new Dictionary<string, bool>
        {
            ["root"] = true,
            ["gap"] = isWhite,
            ["gap_cond"] = onGapPath,
            ["notgap_cond"] = onNotGapPath,
            ["r_gapline"] = onGapPath && condRatioGap,
            ["r_gapspot"] = onGapPath && !condRatioGap,
            ["r_line"] = onNotGapPath && condRatioNotGap,
            ["r_pinhole"] = onNotGapPath && !condRatioNotGap,
            ["r_island"] = !isWhite,
        };
        string resultId = !isWhite ? "r_island"
            : _isInsulGap ? (condRatioGap ? "r_gapline" : "r_gapspot")
            : (condRatioNotGap ? "r_line" : "r_pinhole");

        DrawMapArrow(canvas, map["root"], map["gap"], "TRUE (백)", isWhite, -(map["root"].W / 2 - 20), 0.5);
        DrawMapArrow(canvas, map["root"], map["r_island"], "FALSE (흑)", !isWhite, map["root"].W / 2 - 20, 0.5);
        DrawMapArrow(canvas, map["gap"], map["gap_cond"], "Gap", onGapPath, -70);
        DrawMapArrow(canvas, map["gap"], map["notgap_cond"], "Not Gap", onNotGapPath, 70);
        DrawMapArrow(canvas, map["gap_cond"], map["r_gapline"], "TRUE", onGapPath && condRatioGap, -40);
        DrawMapArrow(canvas, map["gap_cond"], map["r_gapspot"], "FALSE", onGapPath && !condRatioGap, 60);
        DrawMapArrow(canvas, map["notgap_cond"], map["r_line"], "TRUE", onNotGapPath && condRatioNotGap, -40);
        DrawMapArrow(canvas, map["notgap_cond"], map["r_pinhole"], "FALSE", onNotGapPath && !condRatioNotGap, 60);

        foreach (var n in nodes)
            DrawMapNode(canvas, n, conditionIds.Contains(n.Id), onPath.GetValueOrDefault(n.Id), n.Id == resultId, null);

        FinalizeCanvasSize(canvas, nodes);
    }

    // ── 무지부(ELEC_NULL) MAP ── 흑불량만 존재
    private void DrawNullDiagram(Canvas canvas, DefectResult r)
    {
        bool condRatio = r.Ratio > _params.RATIO_B;
        bool condCompact = r.Circularity > _params.COMPACTNESS_B;

        var nodes = new List<MapNode>
        {
            new("root", "무지부 흑불량", 330, 8, 200, 40),
            new("ratio", "Ratio > Ratio_B ?", 280, 100, 220, 56),
            new("r_wrinkle", "NONE_COATING_WRINKLE\n(무지부 주름)", 60, 220, 210, 48),
            new("compact", "Compactness > Compactness_B ?\n(참고용 — 결과는 동일)", 400, 220, 260, 56),
            new("r_island1", "ISLAND", 380, 330, 150, 40),
            new("r_island2", "ISLAND", 560, 330, 150, 40),
        };
        var map = nodes.ToDictionary(n => n.Id);

        var conditionIds = new HashSet<string> { "root", "ratio", "compact" };
        var onPath = new Dictionary<string, bool>
        {
            ["root"] = true,
            ["ratio"] = true,
            ["r_wrinkle"] = condRatio,
            ["compact"] = !condRatio,
            ["r_island1"] = !condRatio && condCompact,
            ["r_island2"] = !condRatio && !condCompact,
        };
        string resultId = condRatio ? "r_wrinkle" : (condCompact ? "r_island1" : "r_island2");

        DrawMapArrow(canvas, map["root"], map["ratio"], null, true, 0, 0.4, true);
        DrawMapArrow(canvas, map["ratio"], map["r_wrinkle"], "TRUE", condRatio, -(map["ratio"].W / 2 - 20));
        DrawMapArrow(canvas, map["ratio"], map["compact"], "FALSE", !condRatio, map["ratio"].W / 2 - 20);
        DrawMapArrow(canvas, map["compact"], map["r_island1"], "TRUE", !condRatio && condCompact, -40);
        DrawMapArrow(canvas, map["compact"], map["r_island2"], "FALSE", !condRatio && !condCompact, 40);

        foreach (var n in nodes)
            DrawMapNode(canvas, n, conditionIds.Contains(n.Id), onPath.GetValueOrDefault(n.Id), n.Id == resultId, null);

        FinalizeCanvasSize(canvas, nodes);
    }

    // ── 코팅부 MAP ── 백/흑 각각 실제 ClassifyDefectType 코팅부 분기를 그대로 따른다.
    private void DrawCoatingDiagram(Canvas canvas, DefectResult r)
    {
        bool isWhite = !r.IsDark;
        double sizeX = r.SizeX;
        double sizeY = r.SizeY;

        bool condRatioW = r.Ratio > _params.RATIO_W;
        bool condSizeYW = sizeY > _params.SIZEY_W;
        bool condLinearW = condRatioW && condSizeYW;
        bool condPeakLine = r.PeakMax >= _params.TH_WHITE_LINE;
        bool condPinhole = r.PeakMax > _params.TH_PINHOLE;
        bool condExtrude = r.PeakMax > _params.TH_EXTRUDE;
        bool condMopol = r.RatioMopol < _params.RATIO_W;

        bool condRatioB = r.Ratio > _params.RATIO_B;
        bool condSizeYB = sizeY > _params.SIZEY_B;
        bool condLinearB = condRatioB && condSizeYB;
        bool condDarkPeak = r.PeakMax > _params.TH_DARK_DEFECT_MIN;
        bool condAngle = r.AngleDeg <= _params.ANGLE_LOW || r.AngleDeg >= _params.ANGLE_HIGH;
        bool condArea = r.Area > _params.BLACK_MIN_AREA;
        bool condCompact = r.Circularity > _params.COMPACTNESS_B;
        bool condPercent = r.AreaObjPercent * 100.0 > _params.PERCENT_B;

        var nodes = new List<MapNode>
        {
            new("root", "IsWhite ?\n(코팅부)", 900, 8, 200, 48),

            // ── 백(White) 분기 ──
            new("w_lin",   "라인성(AND)\nRatio>Ratio_W &\nSizeY>SizeY_W",  40, 110, 230, 64),
            new("w_peakline", "PeakValue >=\nTh_WhiteLine",   0, 230, 200, 56),
            new("w_r_line", "LINE",         0, 350, 150, 44),
            new("w_r_tiny1", "SCRATCH_TINY\n(미세긁힘)", 170, 350, 170, 44),

            new("w_pinhole", "PeakValue >\nTh_Pinhole",  340, 230, 200, 56),
            new("w_r_pinhole", "PINHOLE",   360, 350, 150, 44),
            new("w_extrude", "PeakValue >\nTh_Extrude",  560, 350, 200, 56),
            new("w_r_weak", "WEAK_POINT_W\n(백 약불량)", 800, 470, 170, 56),
            new("w_mopol",  "MopologyRatio <\nRatio_W",  560, 470, 200, 56),
            new("w_r_protrusion", "PROTRUSION\n(돌출/찍힘)", 540, 590, 170, 48),
            new("w_r_tiny2", "SCRATCH_TINY\n(미세긁힘)", 730, 590, 170, 48),

            // ── 흑(Dark) 분기 ──
            new("d_lin",   "라인성(AND)\nRatio>Ratio_B &\nSizeY>SizeY_B", 1040, 110, 230, 64),
            new("d_peak",  "PeakValue >\nTh_DarkDefectMin", 1040, 230, 200, 56),
            new("d_r_weakd", "WEAK_POINT_D\n(흑 약불량)", 1040, 350, 170, 56),
            new("d_angle", "각도 하한 이하\n또는 상한 이상?", 1240, 350, 200, 56),
            new("d_r_scratch", "SCRATCH", 1220, 470, 150, 44),
            new("d_r_crack",   "CRACK",   1390, 470, 150, 44),

            new("d_area",   "Area >\nBlackMinArea", 1700, 230, 200, 56),
            new("d_r_weakd2", "WEAK_POINT_D\n(원본: CRATER의\nUseJudge로 게이트)", 1940, 350, 220, 64),
            new("d_compact", "Compactness >\nCompactness_B", 1700, 350, 200, 56),
            new("d_r_crack_c", "CRACK\n(크랙)", 1940, 470, 190, 56),
            new("d_percent", "AreaRatio% >\nPercent_B", 1700, 470, 200, 56),
            new("d_r_crater", "CRATER\n(분화구)", 1680, 590, 160, 48),
            new("d_r_crack_p", "CRACK\n(크랙)", 1860, 590, 190, 48),
        };
        var map = nodes.ToDictionary(n => n.Id);

        bool onW = isWhite;
        bool onD = !isWhite;

        var conditionIds = new HashSet<string>
        {
            "root", "w_lin", "w_peakline", "w_pinhole", "w_extrude", "w_mopol",
            "d_lin", "d_peak", "d_angle", "d_area", "d_compact", "d_percent"
        };

        var onPath = new Dictionary<string, bool>
        {
            ["root"] = true,
            ["w_lin"] = onW,
            ["w_peakline"] = onW && condLinearW,
            ["w_r_line"] = onW && condLinearW && condPeakLine,
            ["w_r_tiny1"] = onW && condLinearW && !condPeakLine,
            ["w_pinhole"] = onW && !condLinearW,
            ["w_r_pinhole"] = onW && !condLinearW && condPinhole,
            ["w_extrude"] = onW && !condLinearW && !condPinhole,
            ["w_r_weak"] = onW && !condLinearW && !condPinhole && !condExtrude,
            ["w_mopol"] = onW && !condLinearW && !condPinhole && condExtrude,
            ["w_r_protrusion"] = onW && !condLinearW && !condPinhole && condExtrude && condMopol,
            ["w_r_tiny2"] = onW && !condLinearW && !condPinhole && condExtrude && !condMopol,

            ["d_lin"] = onD,
            ["d_peak"] = onD && condLinearB,
            ["d_r_weakd"] = onD && condLinearB && !condDarkPeak,
            ["d_angle"] = onD && condLinearB && condDarkPeak,
            ["d_r_scratch"] = onD && condLinearB && condDarkPeak && condAngle,
            ["d_r_crack"] = onD && condLinearB && condDarkPeak && !condAngle,
            ["d_area"] = onD && !condLinearB,
            ["d_r_weakd2"] = onD && !condLinearB && !condArea,
            ["d_compact"] = onD && !condLinearB && condArea,
            ["d_r_crack_c"] = onD && !condLinearB && condArea && !condCompact,
            ["d_percent"] = onD && !condLinearB && condArea && condCompact,
            ["d_r_crater"] = onD && !condLinearB && condArea && condCompact && condPercent,
            ["d_r_crack_p"] = onD && !condLinearB && condArea && condCompact && !condPercent,
        };

        string resultId;
        if (onW)
        {
            if (condLinearW) resultId = condPeakLine ? "w_r_line" : "w_r_tiny1";
            else if (condPinhole) resultId = "w_r_pinhole";
            else if (condExtrude) resultId = condMopol ? "w_r_protrusion" : "w_r_tiny2";
            else resultId = "w_r_weak";
        }
        else
        {
            if (condLinearB) resultId = condDarkPeak ? (condAngle ? "d_r_scratch" : "d_r_crack") : "d_r_weakd";
            else if (!condArea) resultId = "d_r_weakd2";
            else if (!condCompact) resultId = "d_r_crack_c";
            else resultId = condPercent ? "d_r_crater" : "d_r_crack_p";
        }

        void Arrow(string a, string b, string? label, bool taken, double offset = 0, double midYRatio = 0.5, bool forceGray = false) =>
            DrawMapArrow(canvas, map[a], map[b], label, taken, offset, midYRatio, forceGray);

        // exitOffsetX는 항상 "출발 노드 자신의 폭" 안에서만 줘야 한다 — 그 값이 노드 폭의 절반을 넘으면
        // 화살표 시작점이 박스 바깥 허공에서 출발하는 것처럼 보여 "연결이 안 된 것"처럼 보인다.
        // 목표 노드까지의 실제 이동은 가로 구간(midY)이 담당하므로, 출발 오프셋은 작게만 줘도 충분하다.
        static double LExit(MapNode n) => -(n.W / 2 - 20);
        static double RExit(MapNode n) => (n.W / 2 - 20);

        Arrow("root", "w_lin", "TRUE (백)", onW, LExit(map["root"]), 0.5);
        Arrow("root", "d_lin", "FALSE (흑)", onD, RExit(map["root"]), 0.5);

        Arrow("w_lin", "w_peakline", "TRUE", onW && condLinearW, LExit(map["w_lin"]));
        Arrow("w_lin", "w_pinhole", "FALSE", onW && !condLinearW, RExit(map["w_lin"]), 0.45);
        Arrow("w_peakline", "w_r_line", "TRUE", onW && condLinearW && condPeakLine, -40);
        Arrow("w_peakline", "w_r_tiny1", "FALSE", onW && condLinearW && !condPeakLine, 60);
        Arrow("w_pinhole", "w_r_pinhole", "TRUE", onW && !condLinearW && condPinhole, -40);
        Arrow("w_pinhole", "w_extrude", "FALSE", onW && !condLinearW && !condPinhole, RExit(map["w_pinhole"]), 0.6);
        Arrow("w_extrude", "w_mopol", "TRUE", onW && !condLinearW && !condPinhole && condExtrude, LExit(map["w_extrude"]));
        Arrow("w_extrude", "w_r_weak", "FALSE", onW && !condLinearW && !condPinhole && !condExtrude, RExit(map["w_extrude"]), 0.6);
        Arrow("w_mopol", "w_r_protrusion", "TRUE", onW && !condLinearW && !condPinhole && condExtrude && condMopol, -20);
        Arrow("w_mopol", "w_r_tiny2", "FALSE", onW && !condLinearW && !condPinhole && condExtrude && !condMopol, RExit(map["w_mopol"]));

        Arrow("d_lin", "d_peak", "TRUE", onD && condLinearB, LExit(map["d_lin"]));
        Arrow("d_lin", "d_area", "FALSE", onD && !condLinearB, RExit(map["d_lin"]), 0.4);
        Arrow("d_peak", "d_r_weakd", "FALSE", onD && condLinearB && !condDarkPeak, -30);
        Arrow("d_peak", "d_angle", "TRUE", onD && condLinearB && condDarkPeak, RExit(map["d_peak"]), 0.6);
        Arrow("d_angle", "d_r_scratch", "TRUE", onD && condLinearB && condDarkPeak && condAngle, -20);
        Arrow("d_angle", "d_r_crack", "FALSE", onD && condLinearB && condDarkPeak && !condAngle, RExit(map["d_angle"]));

        Arrow("d_area", "d_r_weakd2", "FALSE", onD && !condLinearB && !condArea, RExit(map["d_area"]), 0.5);
        Arrow("d_area", "d_compact", "TRUE", onD && !condLinearB && condArea, -30);
        Arrow("d_compact", "d_r_crack_c", "FALSE", onD && !condLinearB && condArea && !condCompact, RExit(map["d_compact"]), 0.5);
        Arrow("d_compact", "d_percent", "TRUE", onD && !condLinearB && condArea && condCompact, -30);
        Arrow("d_percent", "d_r_crater", "TRUE", onD && !condLinearB && condArea && condCompact && condPercent, -30);
        Arrow("d_percent", "d_r_crack_p", "FALSE", onD && !condLinearB && condArea && condCompact && !condPercent, RExit(map["d_percent"]));

        foreach (var n in nodes)
        {
            string? tooltip = n.Id switch
            {
                "w_lin" or "d_lin" => $"Ratio={r.Ratio:F2}, SizeY={sizeY:F2}",
                "w_peakline" or "w_pinhole" or "w_extrude" or "d_peak" => $"PeakValue={r.PeakMax:F1}",
                "w_mopol" => $"MopologyRatio={r.RatioMopol:F3}",
                "d_angle" => $"Angle={r.AngleDeg:F1}",
                "d_area" => $"Area={r.Area:F0}, SizeX={sizeX:F2}",
                "d_compact" => $"Compactness={r.Circularity:F3}",
                "d_percent" => $"AreaRatio%={r.AreaObjPercent * 100:F2}",
                _ => null
            };
            DrawMapNode(canvas, n, conditionIds.Contains(n.Id), onPath.GetValueOrDefault(n.Id), n.Id == resultId, tooltip);
        }

        FinalizeCanvasSize(canvas, nodes);
    }
}

// Managed defect result and native struct.
// defectType은 이제 네이티브가 실제 로직(ClassifyDefectType)으로 직접 계산한 RealDefectCodeNative 값이지만,
// Defect List/Feature 패널/Tree/Map은 여전히 ComputeRealDefectCode로 C# 쪽에서 다시 계산해 표시한다
// (판정 근거를 단계별로 보여주기 위해 필요) — 두 계산은 같은 입력값(Ratio/SizeX/SizeY 등)을 쓰므로 항상 일치한다.
public sealed class DefectResult
{
    public int X, Y, Width, Height;
    public double Area, Mean, AspectRatio;
    public bool IsDark, IsLinear;

    public double Circularity;
    public double AngleDeg;
    public double PeakMax;
    public double AreaObjPercent;
    public double RatioMopol;

    // 실제 로직(ClassifyDefectType)이 쓰는 종횡비/실측 크기 — SetDefectInfo와 동일하게 네이티브에서 계산됨.
    public double Ratio;
    public double SizeX;
    public double SizeY;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct DefectResultNative
{
    public int x, y, width, height;
    public double area, mean, aspectRatio;
    public int defectType;
    public int isDark;
    public int isLinear;

    public double areaRatio; // 사용 안 함(구 로직 잔재, 네이티브가 항상 0으로 채움)
    public double circularity;
    public double angleDeg;
    public double peakMax;
    public double areaObjPercent;
    public double ratioMopol;

    public double ratio;
    public double sizeX;
    public double sizeY;

    public DefectResult ToManaged() => new()
    {
        X = x,
        Y = y,
        Width = width,
        Height = height,
        Area = area,
        Mean = mean,
        AspectRatio = aspectRatio,
        IsDark = isDark == 1,
        IsLinear = isLinear == 1,

        Circularity = circularity,
        AngleDeg = angleDeg,
        PeakMax = peakMax,
        AreaObjPercent = areaObjPercent,
        RatioMopol = ratioMopol,

        Ratio = ratio,
        SizeX = sizeX,
        SizeY = sizeY
    };
}

// InspectParams ↔ 네이티브 RealTreeParams 1:1 대응 (native/InspectionAlgorithm/InspectionAlgorithm.h 참고).
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct RealTreeParamsNative
{
    public int TargetBright;
    public int Weight_W;
    public int Weight_B;
    public int BinaryW;
    public int BinaryB;
    public int BinaryNull;
    public int InsulBinaryW;
    public int InsulBinaryB;

    public double Ratio_W;
    public double Ratio_B;
    public double SizeY_W;
    public double SizeY_B;
    public double SizeX_B;

    public double ThPinhole;
    public double ThExtrude;
    public double ThWhiteLine;
    public double ThDarkDefectMin;

    public double BlackMinArea;
    public double CompactnessB;
    public double PercentB;

    public double AngleLow;
    public double AngleHigh;

    public double ScaleX;
    public double ScaleY;

    public int UseGaussian;
}

public static class InspectParamsNativeExtensions
{
    public static RealTreeParamsNative ToNative(this InspectParams p) => new()
    {
        TargetBright = (int)p.TARGET_BRIGHT,
        Weight_W = (int)p.WEIGHT_W,
        Weight_B = (int)p.WEIGHT_B,
        BinaryW = (int)p.BINARY_W,
        BinaryB = (int)p.BINARY_B,
        BinaryNull = (int)p.BINARY_NULL,
        InsulBinaryW = (int)p.INSUL_BINARY_W,
        InsulBinaryB = (int)p.INSUL_BINARY_B,

        Ratio_W = p.RATIO_W,
        Ratio_B = p.RATIO_B,
        SizeY_W = p.SIZEY_W,
        SizeY_B = p.SIZEY_B,
        SizeX_B = p.SIZEX_B,

        ThPinhole = p.TH_PINHOLE,
        ThExtrude = p.TH_EXTRUDE,
        ThWhiteLine = p.TH_WHITE_LINE,
        ThDarkDefectMin = p.TH_DARK_DEFECT_MIN,

        BlackMinArea = p.BLACK_MIN_AREA,
        CompactnessB = p.COMPACTNESS_B,
        PercentB = p.PERCENT_B,

        AngleLow = p.ANGLE_LOW,
        AngleHigh = p.ANGLE_HIGH,

        ScaleX = p.SCALE_X,
        ScaleY = p.SCALE_Y,

        UseGaussian = (int)p.USE_GAUSSIAN
    };
}