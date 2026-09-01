#pragma once

#ifdef INSPECT_EXPORTS
#define INSPECT_API __declspec(dllexport)
#else
#define INSPECT_API __declspec(dllimport)
#endif

extern "C" {
#pragma pack(push, 8)

    struct DefectResult
    {
        int x, y, width, height;
        double area, mean, aspectRatio;
        int defectType;
        int isDark;
        int isLinear;

        double areaRatio;
        double circularity;
        double angleDeg;
        double peakMax;
        double areaObjPercent;
        double ratioMopol;
    };

    // 네이티브에서 런타임으로 설정 가능한 파라미터 구조체
    struct InspectParams
    {
        double D_FORM_MIN_AREA_RATIO;
        double D_ROUNDNESS;
        double D_DARK_AREA_PERCENT;
        double D_LINEAR_BASE_BRIGHT;
        double D_LINE_ANGLE_LOW;
        double D_LINE_ANGLE_HIGH;
        double D_WHITE_PEAK_IF;
        double D_WHITE_PEAK_ELSEIF;
        double D_WHITE_RATIO;
        double D_WHITE_LINE_PEAK;
        double D_LINEARITY_RATIO;
        int AREA_MIN;
        int NDIL_CNT;
    };

#pragma pack(pop)

    INSPECT_API int InspectImage(
        const unsigned char* bgr32, int width, int height, int stride, int threshold,
        DefectResult* results, int maxResults);

    // 파라미터를 런타임에 설정/조회
    INSPECT_API void SetInspectParams(const InspectParams* params);
    INSPECT_API void GetInspectParams(InspectParams* params);

}