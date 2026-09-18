using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace InspectionLogicViewer.Wpf;

/// <summary>
/// 표면 종류. 실제 판정 로직(ClassifyDefectType)이 이 값에 따라 완전히 다른 분기를 탄다.
/// 네이티브 DLL은 표면 종류를 구분하지 않으므로, 사용자가 검사 대상에 맞게 직접 선택한다.
/// </summary>
public enum SurfaceType
{
    Coating,    // 코팅부
    Null,       // 무지부
    Insul       // 절연부
}

/// <summary>
/// 검사 실행 및 판정 로직 설명(다이어그램/파라미터 패널)에 쓰이는 파라미터 묶음.
/// MainWindow, ParamDialog, JSON 저장/로드가 모두 이 하나의 모델을 공유한다.
///
/// 필드 이름은 실제 판정 함수(ClassifyDefectType)에서 쓰는 TREE_PARAM 이름을 그대로 따른다.
/// 전처리(이진화 타깃밝기/가중치 등) 파라미터는 이 뷰어의 네이티브 검사 DLL이 사용하지 않으므로 제외했다.
/// </summary>
public sealed class InspectParams
{
    public double THRESHOLD { get; set; } = 35;

    // 실측 환산 배율 — 네이티브 DLL은 픽셀 단위 Width/Height만 주므로,
    // 실제 로직의 SizeX/SizeY(실측 단위) 비교를 흉내 내기 위해 필요하다.
    public double SCALE_X { get; set; } = 1.0;
    public double SCALE_Y { get; set; } = 1.0;

    // ── 전처리/이진화 (네이티브 SetDefectInfo와 동일한 방식) ──
    public double TARGET_BRIGHT { get; set; } = 128;  // 코팅부 전처리 목표 밝기
    public double WEIGHT_W { get; set; } = 1;          // 코팅부 전처리 백색 가중치
    public double WEIGHT_B { get; set; } = 1;          // 코팅부 전처리 흑색 가중치
    public double BINARY_W { get; set; } = 30;         // 코팅부 백 이진화 임계값
    public double BINARY_B { get; set; } = 30;         // 코팅부 흑 이진화 임계값
    public double BINARY_NULL { get; set; } = 20;      // 무지부 이진화 임계값
    public double INSUL_BINARY_W { get; set; } = 30;   // 절연부 백 이진화 임계값
    public double INSUL_BINARY_B { get; set; } = 30;   // 절연부 흑 이진화 임계값
    public double USE_GAUSSIAN { get; set; } = 1;      // 0/1: 3x3 가우시안 전처리 사용 여부

    // ── 공통 (코팅부/무지부/절연부에서 함께 쓰임) ──
    public double RATIO_W { get; set; } = 3.0;        // 백불량 라인성 종횡비 기준
    public double RATIO_B { get; set; } = 3.0;        // 흑불량 라인성 종횡비 기준
    public double COMPACTNESS_B { get; set; } = 0.60; // 흑불량 원형도 기준

    // ── 코팅부 전용 ──
    public double SIZEY_W { get; set; } = 5.0;   // 백 라인성 최소 세로길이
    public double SIZEY_B { get; set; } = 5.0;   // 흑 라인성 최소 세로길이
    public double SIZEX_B { get; set; } = 3.0;   // 흑 라인성 최대 가로길이(참고용 — 원본 로직상 결과에 영향 없음)
    public double TH_WHITE_LINE { get; set; } = 50.0;    // 라인/미세라인 구분 피크
    public double TH_PINHOLE { get; set; } = 60.0;       // 핀홀 피크 기준
    public double TH_EXTRUDE { get; set; } = 40.0;       // 돌출 피크 기준
    public double TH_DARK_DEFECT_MIN { get; set; } = 40.0; // 흑 선형 최소 피크(약불량 구분)
    public double BLACK_MIN_AREA { get; set; } = 50.0;   // 분화구 판정 최소 면적
    public double PERCENT_B { get; set; } = 5.0;         // 분화구 판정 영역 내 면적비율(%) 기준
    public double ANGLE_LOW { get; set; } = 10.0;        // 스크래치/크랙 구분 각도 하한
    public double ANGLE_HIGH { get; set; } = 80.0;       // 스크래치/크랙 구분 각도 상한

    /// <summary>모델별 판정 사용 여부(UseJudge)를 흉내 낸 스위치. 기본은 전부 사용.</summary>
    public Dictionary<string, bool> UseJudge { get; set; } = RealDefectCatalog.AllCodes.ToDictionary(c => c, _ => true);

    public InspectParams Clone()
    {
        var c = (InspectParams)MemberwiseClone();
        c.UseJudge = new Dictionary<string, bool>(UseJudge);
        return c;
    }

    public static InspectParams LoadFromFile(string path)
    {
        if (!File.Exists(path)) return new InspectParams();
        string json = File.ReadAllText(path);
        var loaded = JsonSerializer.Deserialize<InspectParams>(json);
        if (loaded == null) return new InspectParams();

        // 새로 추가된 판정 코드가 기존 저장 파일에 없을 수 있으므로 기본값으로 채워 넣는다.
        foreach (var code in RealDefectCatalog.AllCodes)
            if (!loaded.UseJudge.ContainsKey(code)) loaded.UseJudge[code] = true;
        return loaded;
    }

    public void SaveToFile(string path)
    {
        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(this, opts));
    }
}

/// <summary>
/// 파라미터 한 개에 대한 표시/편집 메타데이터.
/// 새 파라미터를 추가하려면 ParamCatalog.Fields에 한 줄만 추가하면
/// 요약 패널, 편집 다이얼로그, 하이라이트 매칭이 모두 자동으로 반영된다.
/// </summary>
public sealed class ParamFieldDefinition
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string Format { get; init; } = "F3";
    public bool IsInteger { get; init; }
    /// <summary>ParamDialog에서 같은 그룹끼리 묶어 소제목과 함께 표시하는 데 쓰인다.</summary>
    public string Group { get; init; } = "";
    public required Func<InspectParams, double> Get { get; init; }
    public required Action<InspectParams, double> Set { get; init; }

    public string DisplayValue(InspectParams p) => Get(p).ToString(Format);
}

public static class ParamCatalog
{
    public const string GroupInspection = "검사 실행";
    public const string GroupScale = "실측 환산 배율";
    public const string GroupPreprocess = "전처리 · 이진화";
    public const string GroupCommon = "공통 기준값 (코팅부·무지부·절연부)";
    public const string GroupCoating = "코팅부 전용 기준값";

    public static readonly IReadOnlyList<ParamFieldDefinition> Fields = new List<ParamFieldDefinition>
    {
        new() { Key = "THRESHOLD", Label = "Threshold(이진화 임계값)", Format = "F0", IsInteger = true, Group = GroupInspection, Get = p => p.THRESHOLD, Set = (p, v) => p.THRESHOLD = v },

        new() { Key = "SCALE_X", Label = "가로 실측 배율(ScaleX)", Format = "F3", Group = GroupScale, Get = p => p.SCALE_X, Set = (p, v) => p.SCALE_X = v },
        new() { Key = "SCALE_Y", Label = "세로 실측 배율(ScaleY)", Format = "F3", Group = GroupScale, Get = p => p.SCALE_Y, Set = (p, v) => p.SCALE_Y = v },

        new() { Key = "TARGET_BRIGHT", Label = "TargetBright (코팅부 목표 밝기)", Format = "F0", IsInteger = true, Group = GroupPreprocess, Get = p => p.TARGET_BRIGHT, Set = (p, v) => p.TARGET_BRIGHT = v },
        new() { Key = "WEIGHT_W", Label = "Weight_W (코팅부 백색 가중치)", Format = "F0", IsInteger = true, Group = GroupPreprocess, Get = p => p.WEIGHT_W, Set = (p, v) => p.WEIGHT_W = v },
        new() { Key = "WEIGHT_B", Label = "Weight_B (코팅부 흑색 가중치)", Format = "F0", IsInteger = true, Group = GroupPreprocess, Get = p => p.WEIGHT_B, Set = (p, v) => p.WEIGHT_B = v },
        new() { Key = "BINARY_W", Label = "BinaryW (코팅부 백 이진화 임계값)", Format = "F0", IsInteger = true, Group = GroupPreprocess, Get = p => p.BINARY_W, Set = (p, v) => p.BINARY_W = v },
        new() { Key = "BINARY_B", Label = "BinaryB (코팅부 흑 이진화 임계값)", Format = "F0", IsInteger = true, Group = GroupPreprocess, Get = p => p.BINARY_B, Set = (p, v) => p.BINARY_B = v },
        new() { Key = "BINARY_NULL", Label = "BinaryNull (무지부 이진화 임계값)", Format = "F0", IsInteger = true, Group = GroupPreprocess, Get = p => p.BINARY_NULL, Set = (p, v) => p.BINARY_NULL = v },
        new() { Key = "INSUL_BINARY_W", Label = "InsulBinaryW (절연부 백 이진화 임계값)", Format = "F0", IsInteger = true, Group = GroupPreprocess, Get = p => p.INSUL_BINARY_W, Set = (p, v) => p.INSUL_BINARY_W = v },
        new() { Key = "INSUL_BINARY_B", Label = "InsulBinaryB (절연부 흑 이진화 임계값)", Format = "F0", IsInteger = true, Group = GroupPreprocess, Get = p => p.INSUL_BINARY_B, Set = (p, v) => p.INSUL_BINARY_B = v },
        new() { Key = "USE_GAUSSIAN", Label = "가우시안 전처리 사용(1=사용, 0=사용 안 함)", Format = "F0", IsInteger = true, Group = GroupPreprocess, Get = p => p.USE_GAUSSIAN, Set = (p, v) => p.USE_GAUSSIAN = v },

        new() { Key = "RATIO_W", Label = "Ratio_W (백 종횡비 기준)", Format = "F2", Group = GroupCommon, Get = p => p.RATIO_W, Set = (p, v) => p.RATIO_W = v },
        new() { Key = "RATIO_B", Label = "Ratio_B (흑 종횡비 기준)", Format = "F2", Group = GroupCommon, Get = p => p.RATIO_B, Set = (p, v) => p.RATIO_B = v },
        new() { Key = "COMPACTNESS_B", Label = "Compactness_B (흑 원형도 기준)", Format = "F2", Group = GroupCommon, Get = p => p.COMPACTNESS_B, Set = (p, v) => p.COMPACTNESS_B = v },

        new() { Key = "SIZEY_W", Label = "SizeY_W (백 라인 최소 길이)", Format = "F2", Group = GroupCoating, Get = p => p.SIZEY_W, Set = (p, v) => p.SIZEY_W = v },
        new() { Key = "SIZEY_B", Label = "SizeY_B (흑 라인 최소 길이)", Format = "F2", Group = GroupCoating, Get = p => p.SIZEY_B, Set = (p, v) => p.SIZEY_B = v },
        new() { Key = "SIZEX_B", Label = "SizeX_B (참고용, 결과 영향 없음)", Format = "F2", Group = GroupCoating, Get = p => p.SIZEX_B, Set = (p, v) => p.SIZEX_B = v },
        new() { Key = "TH_WHITE_LINE", Label = "Th_WhiteLine (라인/미세라인 피크)", Format = "F1", Group = GroupCoating, Get = p => p.TH_WHITE_LINE, Set = (p, v) => p.TH_WHITE_LINE = v },
        new() { Key = "TH_PINHOLE", Label = "Th_Pinhole (핀홀 피크)", Format = "F1", Group = GroupCoating, Get = p => p.TH_PINHOLE, Set = (p, v) => p.TH_PINHOLE = v },
        new() { Key = "TH_EXTRUDE", Label = "Th_Extrude (돌출 피크)", Format = "F1", Group = GroupCoating, Get = p => p.TH_EXTRUDE, Set = (p, v) => p.TH_EXTRUDE = v },
        new() { Key = "TH_DARK_DEFECT_MIN", Label = "Th_DarkDefectMin (흑 선형 최소 피크)", Format = "F1", Group = GroupCoating, Get = p => p.TH_DARK_DEFECT_MIN, Set = (p, v) => p.TH_DARK_DEFECT_MIN = v },
        new() { Key = "BLACK_MIN_AREA", Label = "BlackMinArea (분화구 최소 면적)", Format = "F1", Group = GroupCoating, Get = p => p.BLACK_MIN_AREA, Set = (p, v) => p.BLACK_MIN_AREA = v },
        new() { Key = "PERCENT_B", Label = "Percent_B (분화구 영역 내 면적%)", Format = "F2", Group = GroupCoating, Get = p => p.PERCENT_B, Set = (p, v) => p.PERCENT_B = v },
        new() { Key = "ANGLE_LOW", Label = "각도 하한 (스크래치 판정)", Format = "F1", Group = GroupCoating, Get = p => p.ANGLE_LOW, Set = (p, v) => p.ANGLE_LOW = v },
        new() { Key = "ANGLE_HIGH", Label = "각도 상한 (스크래치 판정)", Format = "F1", Group = GroupCoating, Get = p => p.ANGLE_HIGH, Set = (p, v) => p.ANGLE_HIGH = v },
    };

    public static ParamFieldDefinition? Find(string key)
    {
        foreach (var f in Fields)
            if (f.Key == key) return f;
        return null;
    }
}

/// <summary>
/// 실제 판정 로직(ClassifyDefectType)이 반환하는 결과 코드 이름과, 표면 종류별로 어떤 코드들이
/// 쓰이는지에 대한 카탈로그. UseJudge(모델별 판정 사용 여부) 체크박스 UI를 여기서 생성한다.
/// </summary>
public static class RealDefectCatalog
{
    // 절연부
    public const string InsulGapLine = "INSUL_GAP_LINE";
    public const string InsulGapSpot = "INSUL_GAP_SPOT";
    public const string InsulLine = "INSUL_LINE";
    public const string InsulPinhole = "INSUL_PINHOLE";
    public const string InsulIsland = "INSUL_ISLAND";

    // 무지부
    public const string NoneCoatingWrinkle = "NONE_COATING_WRINKLE";
    public const string Island = "ISLAND";

    // 코팅부
    public const string Line = "LINE";
    public const string ScratchTiny = "SCRATCH_TINY";
    public const string Pinhole = "PINHOLE";
    public const string Protrusion = "PROTRUSION";
    public const string WeakPointW = "WEAK_POINT_W";
    public const string Scratch = "SCRATCH";
    public const string Crack = "CRACK";
    public const string WeakPointD = "WEAK_POINT_D";
    public const string Crater = "CRATER";

    public static readonly IReadOnlyList<string> InsulCodes = new[] { InsulGapLine, InsulGapSpot, InsulLine, InsulPinhole, InsulIsland };
    public static readonly IReadOnlyList<string> NullCodes = new[] { NoneCoatingWrinkle, Island };
    public static readonly IReadOnlyList<string> CoatingCodes = new[] { Line, ScratchTiny, Pinhole, Protrusion, WeakPointW, Scratch, Crack, WeakPointD, Crater };

    public static readonly IReadOnlyList<string> AllCodes = new List<string>(InsulCodes.Count + NullCodes.Count + CoatingCodes.Count)
        .Concat(InsulCodes).Concat(NullCodes).Concat(CoatingCodes).ToList();

    /// <summary>사람이 읽는 한글 표시 이름.</summary>
    public static string DisplayName(string code) => code switch
    {
        InsulGapLine => "절연 Gap 라인",
        InsulGapSpot => "절연 Gap 점불량",
        InsulLine => "절연 라인",
        InsulPinhole => "절연 핀홀",
        InsulIsland => "절연 아일랜드",
        NoneCoatingWrinkle => "무지부 주름",
        Island => "아일랜드",
        Line => "라인",
        ScratchTiny => "미세긁힘",
        Pinhole => "핀홀",
        Protrusion => "돌출(찍힘)",
        WeakPointW => "백 약불량",
        Scratch => "스크래치",
        Crack => "크랙",
        WeakPointD => "흑 약불량",
        Crater => "분화구",
        _ => code
    };
}
