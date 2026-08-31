#pragma once

#ifdef INSPECT_EXPORTS
#define INSPECT_API __declspec(dllexport)
#else
#define INSPECT_API __declspec(dllimport)
#endif

extern "C" {

    struct DefectResult
    {
        int x, y, width, height;
        double area;
        double mean;
        double aspectRatio;
        int defectType;
        int isDark;
        int isLinear;
    };

    INSPECT_API int InspectImage(
        const unsigned char* bgr32, int width, int height, int stride, int threshold,
        DefectResult* results, int maxResults);

}