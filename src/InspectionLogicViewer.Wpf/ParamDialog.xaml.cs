using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace InspectionLogicViewer.Wpf;

public partial class ParamDialog : Window
{
    public InspectParams Params { get; private set; }

    private readonly Dictionary<string, TextBox> _fieldBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> _judgeBoxes = new(StringComparer.Ordinal);
    private readonly string _paramsFilePath = Path.Combine(AppContext.BaseDirectory, "inspect_params.json");

    public ParamDialog(InspectParams current)
    {
        InitializeComponent();

        Params = current.Clone();
        BuildFieldRows();
        BuildUseJudgeSection();
        BindValuesToFields(Params);
    }

    // ParamCatalog를 기반으로 라벨+입력창 행을 동적으로 생성한다.
    // 같은 Group끼리는 소제목과 구분선으로 묶어서 보여준다.
    private void BuildFieldRows()
    {
        ParamFieldsHost.Children.Clear();
        _fieldBoxes.Clear();

        string? lastGroup = null;
        foreach (var field in ParamCatalog.Fields)
        {
            if (field.Group != lastGroup)
            {
                if (lastGroup != null)
                    ParamFieldsHost.Children.Add(new Border
                    {
                        Height = 1,
                        Background = (System.Windows.Media.Brush)FindResource("CardBorderBrush"),
                        Margin = new Thickness(0, 4, 0, 16)
                    });

                ParamFieldsHost.Children.Add(new TextBlock
                {
                    Text = field.Group,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
                    Margin = new Thickness(0, 0, 0, 10)
                });
                lastGroup = field.Group;
            }

            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

            var label = new TextBlock
            {
                Text = field.Label,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush")
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var box = new TextBox { Style = (Style)FindResource("FieldTextBox") };
            Grid.SetColumn(box, 1);
            row.Children.Add(box);

            _fieldBoxes[field.Key] = box;
            ParamFieldsHost.Children.Add(row);
        }
    }

    // 실제 판정 로직의 UseJudge(모델별 판정 사용 여부)를 흉내 낸 체크박스 섹션.
    // 표면 종류(절연부/무지부/코팅부)별로 묶어서 보여준다.
    private void BuildUseJudgeSection()
    {
        _judgeBoxes.Clear();

        ParamFieldsHost.Children.Add(new Border
        {
            Height = 1,
            Background = (System.Windows.Media.Brush)FindResource("CardBorderBrush"),
            Margin = new Thickness(0, 4, 0, 16)
        });
        ParamFieldsHost.Children.Add(new TextBlock
        {
            Text = "판정 사용 여부 (UseJudge)",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            Margin = new Thickness(0, 0, 0, 10)
        });

        AddJudgeGroup("절연부", RealDefectCatalog.InsulCodes);
        AddJudgeGroup("무지부", RealDefectCatalog.NullCodes);
        AddJudgeGroup("코팅부", RealDefectCatalog.CoatingCodes);
    }

    private void AddJudgeGroup(string groupLabel, IReadOnlyList<string> codes)
    {
        ParamFieldsHost.Children.Add(new TextBlock
        {
            Text = groupLabel,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });

        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        foreach (var code in codes)
        {
            var box = new CheckBox
            {
                Content = $"{RealDefectCatalog.DisplayName(code)} ({code})",
                Margin = new Thickness(0, 0, 16, 8),
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush")
            };
            _judgeBoxes[code] = box;
            wrap.Children.Add(box);
        }
        ParamFieldsHost.Children.Add(wrap);
    }

    private void BindValuesToFields(InspectParams p)
    {
        foreach (var field in ParamCatalog.Fields)
            _fieldBoxes[field.Key].Text = field.Get(p).ToString("G");

        foreach (var (code, box) in _judgeBoxes)
            box.IsChecked = !p.UseJudge.TryGetValue(code, out var v) || v;
    }

    private bool TryReadFieldsInto(InspectParams p)
    {
        foreach (var field in ParamCatalog.Fields)
        {
            if (!double.TryParse(_fieldBoxes[field.Key].Text, out var value))
            {
                MessageBox.Show($"'{field.Label}' 값이 올바르지 않습니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            field.Set(p, value);
        }

        foreach (var (code, box) in _judgeBoxes)
            p.UseJudge[code] = box.IsChecked == true;

        return true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadFieldsInto(Params)) return;
        DialogResult = true;
        Close();
    }

    private void SaveJson_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadFieldsInto(Params)) return;

        try
        {
            Params.SaveToFile(_paramsFilePath);
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
            Params = InspectParams.LoadFromFile(_paramsFilePath);
            BindValuesToFields(Params);
            MessageBox.Show("JSON에서 파라미터를 불러왔습니다.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파라미터 불러오기 실패: {ex.Message}");
        }
    }
}
