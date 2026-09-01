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
        double area, mean, aspectRatio;
        int defectType;
        int isDark;
        int isLinear;

        // ---- doc11 판정 로직과 동기화하기 위한 실제 특징값 ----
        double areaRatio;      // B-면적비율 (blob면적/bbox면적)
        double circularity;    // 진원도 (Compactness)
        double angleDeg;       // 기울기(각도)
        double peakMax;        // 최대 편차 피크치
        double areaObjPercent; // 이미지 대비 면적%
        double ratioMopol;     // dRatio_mopol
    };

    INSPECT_API int InspectImage(
        const unsigned char* bgr32, int width, int height, int stride, int threshold,
        DefectResult* results, int maxResults);

}