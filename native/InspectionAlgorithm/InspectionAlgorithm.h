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

#pragma pack(pop)

    INSPECT_API int InspectImage(
        const unsigned char* bgr32, int width, int height, int stride, int threshold,
        DefectResult* results, int maxResults);

}