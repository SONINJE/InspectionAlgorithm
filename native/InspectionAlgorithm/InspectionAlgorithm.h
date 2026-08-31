#pragma once
#ifdef INSPECTIONALGORITHM_EXPORTS
#define INSPECT_API __declspec(dllexport)
#else
#define INSPECT_API __declspec(dllimport)
#endif
extern "C" {
struct DefectResult {
    int x,y,width,height;
    double area,mean,aspectRatio;
    int defectType;
    int isDark;    // 1 = dark candidate, 0 = white candidate
    int isLinear;  // 1 = linear, 0 = non-linear
};
INSPECT_API int InspectImage(const unsigned char* bgr32,int width,int height,int stride,int threshold,
                             DefectResult* results,int maxResults);
}