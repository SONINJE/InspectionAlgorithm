using System;
using System.Windows;
using System.IO;
using System.Text.Json;

namespace InspectionLogicViewer.Wpf;

public partial class ParamDialog : Window
{
    public double D_FORM_MIN_AREA_RATIO { get; private set; }
    public double D_ROUNDNESS { get; private set; }
    public double D_DARK_AREA_PERCENT { get; private set; }
    public double D_LINEAR_BASE_BRIGHT { get; private set; }
    public double D_LINE_ANGLE_LOW { get; private set; }
    public double D_LINE_ANGLE_HIGH { get; private set; }
    public double D_WHITE_PEAK_IF { get; private set; }
    public double D_WHITE_PEAK_ELSEIF { get; private set; }
    public double D_WHITE_RATIO { get; private set; }
    public double D_WHITE_LINE_PEAK { get; private set; }
    public double D_LINEARITY_RATIO { get; private set; }
    public int AREA_MIN { get; private set; }
    public int NDIL_CNT { get; private set; }

    private readonly string _paramsFilePath = Path.Combine(AppContext.BaseDirectory, "inspect_params.json");

    public ParamDialog(
        double formMinAreaRatio,
        double roundness,
        double darkAreaPercent,
        double linearBaseBright,
        double lineAngleLow,
        double lineAngleHigh,
        double whitePeakIf,
        double whitePeakElseIf,
        double whiteRatio,
        double whiteLinePeak,
        double linearityRatio,
        int areaMin,
        int ndilCnt)
    {
        InitializeComponent();

        D_FORM_MIN_AREA_RATIO = formMinAreaRatio;
        D_ROUNDNESS = roundness;
        D_DARK_AREA_PERCENT = darkAreaPercent;
        D_LINEAR_BASE_BRIGHT = linearBaseBright;
        D_LINE_ANGLE_LOW = lineAngleLow;
        D_LINE_ANGLE_HIGH = lineAngleHigh;
        D_WHITE_PEAK_IF = whitePeakIf;
        D_WHITE_PEAK_ELSEIF = whitePeakElseIf;
        D_WHITE_RATIO = whiteRatio;
        D_WHITE_LINE_PEAK = whiteLinePeak;
        D_LINEARITY_RATIO = linearityRatio;
        AREA_MIN = areaMin;
        NDIL_CNT = ndilCnt;

        // 초기값 채우기
        Txt_FormMinAreaRatio.Text = D_FORM_MIN_AREA_RATIO.ToString("G");
        Txt_Roundness.Text = D_ROUNDNESS.ToString("G");
        Txt_DarkAreaPercent.Text = D_DARK_AREA_PERCENT.ToString("G");
        Txt_LinearBaseBright.Text = D_LINEAR_BASE_BRIGHT.ToString("G");
        Txt_LineAngleLow.Text = D_LINE_ANGLE_LOW.ToString("G");
        Txt_LineAngleHigh.Text = D_LINE_ANGLE_HIGH.ToString("G");
        Txt_WhitePeakIf.Text = D_WHITE_PEAK_IF.ToString("G");
        Txt_WhitePeakElseIf.Text = D_WHITE_PEAK_ELSEIF.ToString("G");
        Txt_WhiteRatio.Text = D_WHITE_RATIO.ToString("G");
        Txt_WhiteLinePeak.Text = D_WHITE_LINE_PEAK.ToString("G");
        Txt_LinearityRatio.Text = D_LINEARITY_RATIO.ToString("G");
        Txt_AreaMin.Text = AREA_MIN.ToString();
        Txt_NdilCnt.Text = NDIL_CNT.ToString();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(Txt_FormMinAreaRatio.Text, out var dv)) D_FORM_MIN_AREA_RATIO = dv;
        if (double.TryParse(Txt_Roundness.Text, out dv)) D_ROUNDNESS = dv;
        if (double.TryParse(Txt_DarkAreaPercent.Text, out dv)) D_DARK_AREA_PERCENT = dv;
        if (double.TryParse(Txt_LinearBaseBright.Text, out dv)) D_LINEAR_BASE_BRIGHT = dv;
        if (double.TryParse(Txt_LineAngleLow.Text, out dv)) D_LINE_ANGLE_LOW = dv;
        if (double.TryParse(Txt_LineAngleHigh.Text, out dv)) D_LINE_ANGLE_HIGH = dv;
        if (double.TryParse(Txt_WhitePeakIf.Text, out dv)) D_WHITE_PEAK_IF = dv;
        if (double.TryParse(Txt_WhitePeakElseIf.Text, out dv)) D_WHITE_PEAK_ELSEIF = dv;
        if (double.TryParse(Txt_WhiteRatio.Text, out dv)) D_WHITE_RATIO = dv;
        if (double.TryParse(Txt_WhiteLinePeak.Text, out dv)) D_WHITE_LINE_PEAK = dv;
        if (double.TryParse(Txt_LinearityRatio.Text, out dv)) D_LINEARITY_RATIO = dv;
        if (int.TryParse(Txt_AreaMin.Text, out var iv)) AREA_MIN = iv;
        if (int.TryParse(Txt_NdilCnt.Text, out iv)) NDIL_CNT = iv;

        DialogResult = true;
        Close();
    }





    // JSON 저장/로드 (다이얼로그에서 직접)
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

    private void SaveJson_Click(object sender, RoutedEventArgs e)
    {
        // 먼저 화면의 값을 읽음
        Ok_Click(sender, null);

        var p = new InspectParamsJson
        {
            D_FORM_MIN_AREA_RATIO = D_FORM_MIN_AREA_RATIO,
            D_ROUNDNESS = D_ROUNDNESS,
            D_DARK_AREA_PERCENT = D_DARK_AREA_PERCENT,
            D_LINEAR_BASE_BRIGHT = D_LINEAR_BASE_BRIGHT,
            D_LINE_ANGLE_LOW = D_LINE_ANGLE_LOW,
            D_LINE_ANGLE_HIGH = D_LINE_ANGLE_HIGH,
            D_WHITE_PEAK_IF = D_WHITE_PEAK_IF,
            D_WHITE_PEAK_ELSEIF = D_WHITE_PEAK_ELSEIF,
            D_WHITE_RATIO = D_WHITE_RATIO,
            D_WHITE_LINE_PEAK = D_WHITE_LINE_PEAK,
            D_LINEARITY_RATIO = D_LINEARITY_RATIO,
            AREA_MIN = AREA_MIN,
            NDIL_CNT = NDIL_CNT
        };

        try
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(p, opts);
            File.WriteAllText(_paramsFilePath, json);
            MessageBox.Show("파라미터를 JSON에 저장했습니다.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파라미터 저장 실패: {ex.Message}");
        }
    }

    private void LoadJson_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_paramsFilePath)) { MessageBox.Show("저장된 JSON 파일이 없습니다."); return; }

        try
        {
            string json = File.ReadAllText(_paramsFilePath);
            var p = JsonSerializer.Deserialize<InspectParamsJson>(json);
            if (p == null) { MessageBox.Show("JSON 파싱 실패"); return; }

            Txt_FormMinAreaRatio.Text = p.D_FORM_MIN_AREA_RATIO.ToString("G");
            Txt_Roundness.Text = p.D_ROUNDNESS.ToString("G");
            Txt_DarkAreaPercent.Text = p.D_DARK_AREA_PERCENT.ToString("G");
            Txt_LinearBaseBright.Text = p.D_LINEAR_BASE_BRIGHT.ToString("G");
            Txt_LineAngleLow.Text = p.D_LINE_ANGLE_LOW.ToString("G");
            Txt_LineAngleHigh.Text = p.D_LINE_ANGLE_HIGH.ToString("G");
            Txt_WhitePeakIf.Text = p.D_WHITE_PEAK_IF.ToString("G");
            Txt_WhitePeakElseIf.Text = p.D_WHITE_PEAK_ELSEIF.ToString("G");
            Txt_WhiteRatio.Text = p.D_WHITE_RATIO.ToString("G");
            Txt_WhiteLinePeak.Text = p.D_WHITE_LINE_PEAK.ToString("G");
            Txt_LinearityRatio.Text = p.D_LINEARITY_RATIO.ToString("G");
            Txt_AreaMin.Text = p.AREA_MIN.ToString();
            Txt_NdilCnt.Text = p.NDIL_CNT.ToString();

            MessageBox.Show("JSON에서 파라미터를 불러왔습니다.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파라미터 불러오기 실패: {ex.Message}");
        }
    }
}