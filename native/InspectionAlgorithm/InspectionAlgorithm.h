#pragma once

#ifdef INSPECT_EXPORTS
#define INSPECT_API __declspec(dllexport)
#else
#define INSPECT_API __declspec(dllimport)
#endif

extern "C" {
#pragma pack(push, 8)

    // 표면 종류. C# InspectionLogicViewer.Wpf.SurfaceType과 순서를 맞춘다 (Coating=0, Null=1, Insul=2).
    enum SurfaceTypeNative
    {
        SURF_COATING = 0,
        SURF_NULL = 1,
        SURF_INSUL = 2
    };

    // ClassifyDefectType이 반환하는 실제 판정 코드. C# RealDefectCatalog와 순서를 맞춘다.
    enum RealDefectCodeNative
    {
        RDC_UNKNOWN = 0,
        RDC_INSUL_GAP_LINE = 1,
        RDC_INSUL_GAP_SPOT = 2,
        RDC_INSUL_LINE = 3,
        RDC_INSUL_PINHOLE = 4,
        RDC_INSUL_ISLAND = 5,
        RDC_NONE_COATING_WRINKLE = 6,
        RDC_ISLAND = 7,
        RDC_LINE = 8,
        RDC_SCRATCH_TINY = 9,
        RDC_PINHOLE = 10,
        RDC_PROTRUSION = 11,
        RDC_WEAK_POINT_W = 12,
        RDC_SCRATCH = 13,
        RDC_CRACK = 14,
        RDC_WEAK_POINT_D = 15,
        RDC_CRATER = 16
    };

    struct DefectResult
    {
        int x, y, width, height;
        double area, mean, aspectRatio;
        int defectType;    // RealDefectCodeNative 값
        int isDark;
        int isLinear;

        double areaRatio;
        double circularity;   // Compactness
        double angleDeg;
        double peakMax;        // PeakValue
        double areaObjPercent; // AreaRatioWithinRoi (0~1 비율)
        double ratioMopol;     // MopologyRatio

        // 실제 로직(ClassifyDefectType)이 쓰는 종횡비/실측 크기 — SetDefectInfo와 동일한 방식으로 계산됨.
        double ratio;   // 장축/단축 비율 (항상 1 이상)
        double sizeX;   // 실측 가로 크기
        double sizeY;   // 실측 세로 크기
    };

    // ClassifyDefectType(및 전처리/이진화)에 쓰이는 TREE_PARAM 임계값. C#의 InspectParams와 1:1 대응.
    struct RealTreeParams
    {
        int TargetBright;
        int Weight_W;
        int Weight_B;
        int BinaryW;
        int BinaryB;
        int BinaryNull;
        int InsulBinaryW;
        int InsulBinaryB;

        double Ratio_W;
        double Ratio_B;
        double SizeY_W;
        double SizeY_B;
        double SizeX_B;

        double ThPinhole;
        double ThExtrude;
        double ThWhiteLine;
        double ThDarkDefectMin;

        double BlackMinArea;
        double CompactnessB;
        double PercentB;

        double AngleLow;
        double AngleHigh;

        double ScaleX;
        double ScaleY;

        int UseGaussian; // 0=사용 안 함, 1=3x3 가우시안 전처리 사용
    };

#pragma pack(pop)

    // surfaceType: SurfaceTypeNative, isInsulGap: 절연 Gap 영역 여부(0/1, 절연부일 때만 의미 있음).
    INSPECT_API int InspectImage(
        const unsigned char* bgr32, int width, int height, int stride, int threshold,
        int surfaceType, int isInsulGap,
        DefectResult* results, int maxResults);

    // 실제 판정 로직 파라미터를 런타임에 설정/조회
    INSPECT_API void SetRealTreeParams(const RealTreeParams* params);
    INSPECT_API void GetRealTreeParams(RealTreeParams* params);
}
