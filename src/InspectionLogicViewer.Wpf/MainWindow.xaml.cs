using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Collections.Generic;
using System;

namespace InspectionLogicViewer.Wpf;

public partial class MainWindow : Window
{
    private BitmapSource? _source;
    private readonly List<DefectResult> _results = new();

    public MainWindow() => InitializeComponent();

    private void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Image|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff" };
        if (d.ShowDialog() != true) return;

        using var s = File.OpenRead(d.FileName);
        var decoder = BitmapDecoder.Create(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        _source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgr32, null, 0);
        ImageView.Source = _source;
        _results.Clear(); DefectList.Items.Clear(); LogicTreeView.Items.Clear(); PathList.Items.Clear();
        StatusText.Text = $"{System.IO.Path.GetFileName(d.FileName)}  {_source.PixelWidth} x {_source.PixelHeight}";
    }

    private void Inspect_Click(object sender, RoutedEventArgs e)
    {
        if (_source == null) { MessageBox.Show("먼저 이미지를 열어주세요."); return; }
        if (!int.TryParse(ThresholdBox.Text, out int threshold)) threshold = 35;

        try
        {
            System.Runtime.InteropServices.NativeLibrary.Load("InspectionAlgorithm.dll");
        }
        catch (System.DllNotFoundException ex)
        {
            MessageBox.Show($"네이티브 DLL을 로드할 수 없음: {ex.Message}\n확인: DLL이 출력 폴더에 있는지, x64로 빌드되었는지 확인하세요.");
            return;
        }
        catch (System.BadImageFormatException ex)
        {
            MessageBox.Show($"네이티브 DLL 아키텍처 불일치: {ex.Message}\n확인: 프로세스(x64)와 DLL(x86/x64)이 일치하는지 확인하세요.");
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"네이티브 DLL 로드 중 오류: {ex.Message}");
            return;
        }

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
        finally { h.Free(); }

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
        Fx.Text = r.X.ToString(); Fy.Text = r.Y.ToString(); Fw.Text = r.Width.ToString(); Fh.Text = r.Height.ToString();
        Farea.Text = r.Area.ToString("F0"); Fmean.Text = r.Mean.ToString("F1");
        Faspect.Text = r.AspectRatio.ToString("F2"); Ftype.Text = r.DefectTypeName;
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
                // optional: show crop if desired
                // CropPreview.Source = cb;
                // CropInfo.Text = $"{cx},{cy}  {cw}x{ch}";
            }
            catch
            {
                //CropPreview.Source = null;
                //CropInfo.Text = "Crop 불가";
            }
        }
    }

    private void BuildLogicTree(DefectResult r)
    {
        LogicTreeView.Items.Clear(); PathList.Items.Clear();
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
                var a = AddCondition(nodeLine, "불량 흑 면적비율 > PARA 형태 판단 최소면", r.AreaRatio, r.AreaRatio > 0.25);
                var b = AddCondition(a, "불량 진원도 > PARA 흑 진원도", r.Circularity, r.Circularity > 0.60);
                var c = AddCondition(b, "불량 면적% > PARA Dark면적%", r.AreaObjPercent, r.AreaObjPercent > 0.05);
                AddResult(c, "분화구 / 크랙 / WEAK_POINT_D");
            }
            else
            {
                var a = AddCondition(nodeLine, "다크 피크치 > PARA 선형 불량 기준밝기", r.PeakMax, r.PeakMax > 40.0);
                var b = AddCondition(a, "불량각도 < PARA 10도 or >90도", r.AngleDeg, r.AngleDeg < 10.0 || r.AngleDeg > 90.0);
                AddResult(b, "스크래치 / 크랙 / PARA 흑 약불량");
            }
        }
        else
        {
            var nodeWhite = AddCondition(root, "화이트성", "TRUE", true);
            var nodeLineW = AddCondition(nodeWhite, "라인성", isLinear ? "TRUE" : "FALSE", isLinear);

            // white: 다크와 동일한 구조 — 라인성 TRUE/FALSE로 분기
            if (!isLinear)
            {
                // 비선형(라인성 == FALSE) : IF / ELSE IF / ELSE
                var ifNode = AddCondition(nodeLineW, "PARA 불량 PEAK > WHITE_PEAK (IF)", r.PeakMax, r.PeakMax > 60.0);
                if (r.PeakMax > 60.0) AddResult(ifNode, "핀홀");

                var elifNode = AddCondition(nodeLineW, "PARA 불량 PEAK > WHITE_PEAK (Else if)", r.PeakMax, r.PeakMax > 40.0 && r.PeakMax <= 60.0);
                if (r.PeakMax > 40.0 && r.PeakMax <= 60.0)
                {
                    var ratioNode = AddCondition(elifNode, "dRatio_mopol > Ratio<White>", r.RatioMopol, r.RatioMopol > 1.5);
                    AddResult(ratioNode, r.RatioMopol > 1.5 ? "미세긁힘" : "찍힘");
                }

                if (r.PeakMax <= 40.0)
                {
                    var elseNode = AddCondition(nodeLineW, "화이트 약불량 (Else)", r.PeakMax, true);
                    AddResult(elseNode, "화이트 약불량");
                }
            }
            else
            {
                // 선형(라인성 == TRUE) : Value(라인) 조건으로 이동
                var a = AddCondition(nodeLineW, "Value(라인) < WHITE_PEAK", r.PeakMax, r.PeakMax < 50.0);
                AddResult(a, r.PeakMax < 50.0 ? "라인 / 미세긁힘(라인)" : "미세긁힘");
            }
        }

        PathList.Items.Add($"DefectType={r.DefectType} IsDark={isDark} IsLinear={isLinear}");
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

    // 다이어그램을 UI 내 캔버스에 그리는 함수
    // - 각 조건 노드는 실제 판정(true/false)에 따라 채움색을 녹색/빨강으로 표시
    // - 결과 노드는 판정 경로에 포함되면 진하게 표시
    private void DrawLogicDiagramInCanvas(Canvas canvas, DefectResult r)
    {
        if (canvas == null) return;
        canvas.Children.Clear();

        // ================= doc11(네이티브 DLL)과 완전히 동일한 임계값 =================
        const double D_FORM_MIN_AREA_RATIO = 0.25;
        const double D_ROUNDNESS = 0.60;
        const double D_DARK_AREA_PERCENT = 0.05;
        const double D_LINEAR_BASE_BRIGHT = 40.0;
        const double D_LINE_ANGLE_LOW = 10.0;
        const double D_LINE_ANGLE_HIGH = 90.0;

        const double D_WHITE_PEAK_IF = 60.0;
        const double D_WHITE_PEAK_ELSEIF = 40.0;
        const double D_WHITE_RATIO = 1.50;
        const double D_WHITE_LINE_PEAK = 50.0;

        bool isDark = r.IsDark;
        bool isLinear = r.IsLinear;

        bool condFormArea = r.AreaRatio > D_FORM_MIN_AREA_RATIO;
        bool condRoundness = r.Circularity > D_ROUNDNESS;
        bool condDarkAreaPct = r.AreaObjPercent > D_DARK_AREA_PERCENT;
        bool condDarkPeak = r.PeakMax > D_LINEAR_BASE_BRIGHT;
        bool condAngle = (r.AngleDeg < D_LINE_ANGLE_LOW) || (r.AngleDeg > D_LINE_ANGLE_HIGH);

        bool condWVal1_If = r.PeakMax > D_WHITE_PEAK_IF;
        bool condWVal1_ElseIf = r.PeakMax > D_WHITE_PEAK_ELSEIF;
        bool condWhiteRatio = r.RatioMopol > D_WHITE_RATIO;
        bool condWhiteLine = r.PeakMax < D_WHITE_LINE_PEAK;

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

        // ---- 박스 색칠 결정용 (실제로 이 노드가 판정 경로 위에 있는지) ----
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

        // ---- 화살표(edge) 단위로 실제 선택 여부를 명시 ----
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

        // ---- 화살표(edge) 자체가 "조건 노드"인지 판정용 (박스 색은 nodeOnPath만 사용) ----
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

[StructLayout(LayoutKind.Sequential)]
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
        AspectRatio = aspectratio(),
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

    private double aspectratio() => aspectratio_field();
    private double aspectratio_field() { return aspectratio_internal; }
    private double aspectratio_internal;
}