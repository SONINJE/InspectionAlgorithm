#define INSPECT_EXPORTS
#include <windows.h>          // 명시적으로 가장 먼저
#define NOMINMAX              // windows.h의 min/max 매크로가 std::min/max와 충돌하는 것 방지
#include "InspectionAlgorithm.h"
#include "Fchain.h"      // CChain 클래스
#include <vector>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <memory>  

// 기본값 (원래의 constexpr 값들)
static constexpr double DEF_D_FORM_MIN_AREA_RATIO = 0.25;
static constexpr double DEF_D_ROUNDNESS = 0.60;
static constexpr double DEF_D_DARK_AREA_PERCENT = 0.05;
static constexpr double DEF_D_LINEAR_BASE_BRIGHT = 40.0;
static constexpr double DEF_D_LINE_ANGLE_LOW = 10.0;
static constexpr double DEF_D_LINE_ANGLE_HIGH = 90.0;

static constexpr double DEF_D_WHITE_PEAK_IF = 60.0;
static constexpr double DEF_D_WHITE_PEAK_ELSEIF = 40.0;
static constexpr double DEF_D_WHITE_RATIO = 1.50;
static constexpr double DEF_D_WHITE_LINE_PEAK = 50.0;

static constexpr double DEF_D_LINEARITY_RATIO = 3.0;
static constexpr int    DEF_AREA_MIN = 4;
static constexpr int    DEF_NDIL_CNT = 2;

// 런타임에서 변경 가능한 전역 변수
static double g_D_FORM_MIN_AREA_RATIO = DEF_D_FORM_MIN_AREA_RATIO;
static double g_D_ROUNDNESS = DEF_D_ROUNDNESS;
static double g_D_DARK_AREA_PERCENT = DEF_D_DARK_AREA_PERCENT;
static double g_D_LINEAR_BASE_BRIGHT = DEF_D_LINEAR_BASE_BRIGHT;
static double g_D_LINE_ANGLE_LOW = DEF_D_LINE_ANGLE_LOW;
static double g_D_LINE_ANGLE_HIGH = DEF_D_LINE_ANGLE_HIGH;

static double g_D_WHITE_PEAK_IF = DEF_D_WHITE_PEAK_IF;
static double g_D_WHITE_PEAK_ELSEIF = DEF_D_WHITE_PEAK_ELSEIF;
static double g_D_WHITE_RATIO = DEF_D_WHITE_RATIO;
static double g_D_WHITE_LINE_PEAK = DEF_D_WHITE_LINE_PEAK;

static double g_D_LINEARITY_RATIO = DEF_D_LINEARITY_RATIO;
static int    g_AREA_MIN = DEF_AREA_MIN;
static int    g_NDIL_CNT = DEF_NDIL_CNT;

enum DefectType
{
    DEFECT_UNKNOWN = 0,
    DEFECT_SCRATCH = 1,
    DEFECT_CRATER = 10,
    DEFECT_CRACK = 11,
    DEFECT_WEAK_POINT_D = 12,
    DEFECT_BLACK_WEAK = 13,
    DEFECT_PINHOLE = 20,
    DEFECT_MICRO_SCRATCH = 21,
    DEFECT_DENT = 22,
    DEFECT_WHITE_WEAK = 23,
    DEFECT_LINE = 30
};

// blob 하나에서 추출한 특징값
struct BlobFeature
{
    int    x, y, w, h;
    double area;
    double areaRatio;      // B-면적비율 (blob면적 / bbox면적)
    double circularity;    // 진원도 (Compactness)
    double angleDeg;       // 기울기
    double peakMin;        // 최소 편차 피크
    double peakMax;        // 최대 편차 피크
    double areaObjPercent; // 이미지 대비 면적%
    double ratioMopol;     // dRatio_mopol (핀홀류 모폴로지 후 세로:가로 비율)
    double ratio;          // 세로:가로 비율 (라인성 판단용)
    bool   isDark;         // true=다크(black), false=화이트(white)
};

// ============================================================
// 라인성 판단
// ============================================================
static bool IsLinearBlob(const BlobFeature& f)
{
    return f.ratio > g_D_LINEARITY_RATIO;
}

static int Dilate_BinaryMini(LPBYTE fmSour, LPBYTE fmDest, int nWidth, int nHeight, int nPitch)
{
    int nOrgX{}, nOrgY{};
    int nKernelSizeY{}, nKernelSizeX{};
    nKernelSizeY = nKernelSizeX = 3;
    int kernel[9]{};
    for (int i = 0; i < 9; i++) kernel[i] = 1;

    nOrgX = static_cast<int>(nKernelSizeX / 2.0 - 0.5);
    nOrgY = static_cast<int>(nKernelSizeY / 2.0 - 0.5);

    for (int i = 0; i < nHeight - nKernelSizeY; i++) {
        for (int j = 0; j < nWidth - nKernelSizeX; j++) {
            for (int k = 0; k < nKernelSizeY; k++) {
                for (int l = 0; l < nKernelSizeX; l++) {
                    if (*(fmSour + (i + k) * nWidth + j + l)) {
                        *(fmDest + (i + nOrgY) * nWidth + j + nOrgX) = 255;
                        goto LOOP;
                    }
                    *(fmDest + (i + nOrgY) * nWidth + j + nOrgX) = 0;
                }
            }
        LOOP: continue;
        }
    }

    return 1;
}

// ============================================================
// 런타임 파라미터 설정/조회
// ============================================================
extern "C" INSPECT_API void SetInspectParams(const InspectParams* params)
{
    if (!params) return;
    g_D_FORM_MIN_AREA_RATIO = params->D_FORM_MIN_AREA_RATIO;
    g_D_ROUNDNESS = params->D_ROUNDNESS;
    g_D_DARK_AREA_PERCENT = params->D_DARK_AREA_PERCENT;
    g_D_LINEAR_BASE_BRIGHT = params->D_LINEAR_BASE_BRIGHT;
    g_D_LINE_ANGLE_LOW = params->D_LINE_ANGLE_LOW;
    g_D_LINE_ANGLE_HIGH = params->D_LINE_ANGLE_HIGH;

    g_D_WHITE_PEAK_IF = params->D_WHITE_PEAK_IF;
    g_D_WHITE_PEAK_ELSEIF = params->D_WHITE_PEAK_ELSEIF;
    g_D_WHITE_RATIO = params->D_WHITE_RATIO;
    g_D_WHITE_LINE_PEAK = params->D_WHITE_LINE_PEAK;

    g_D_LINEARITY_RATIO = params->D_LINEARITY_RATIO;
    g_AREA_MIN = params->AREA_MIN;
    g_NDIL_CNT = params->NDIL_CNT;
}

extern "C" INSPECT_API void GetInspectParams(InspectParams* params)
{
    if (!params) return;
    params->D_FORM_MIN_AREA_RATIO = g_D_FORM_MIN_AREA_RATIO;
    params->D_ROUNDNESS = g_D_ROUNDNESS;
    params->D_DARK_AREA_PERCENT = g_D_DARK_AREA_PERCENT;
    params->D_LINEAR_BASE_BRIGHT = g_D_LINEAR_BASE_BRIGHT;
    params->D_LINE_ANGLE_LOW = g_D_LINE_ANGLE_LOW;
    params->D_LINE_ANGLE_HIGH = g_D_LINE_ANGLE_HIGH;

    params->D_WHITE_PEAK_IF = g_D_WHITE_PEAK_IF;
    params->D_WHITE_PEAK_ELSEIF = g_D_WHITE_PEAK_ELSEIF;
    params->D_WHITE_RATIO = g_D_WHITE_RATIO;
    params->D_WHITE_LINE_PEAK = g_D_WHITE_LINE_PEAK;

    params->D_LINEARITY_RATIO = g_D_LINEARITY_RATIO;
    params->AREA_MIN = g_AREA_MIN;
    params->NDIL_CNT = g_NDIL_CNT;
}

// ============================================================
// 로직트리 기반 최종 불량 타입 판정 (기존 코드에서 기본 상수 대신 전역변수 사용)
// ============================================================
static int ClassifyDefectType(const BlobFeature& f, bool bIsLinear)
{
    int nResultType = DEFECT_UNKNOWN;

    if (f.isDark)
    {
        if (!bIsLinear)
        {
            if (f.areaRatio > g_D_FORM_MIN_AREA_RATIO)
            {
                if (f.circularity > g_D_ROUNDNESS)
                {
                    if (f.areaObjPercent > g_D_DARK_AREA_PERCENT)
                    {
                        nResultType = DEFECT_CRATER;
                    }
                    else
                    {
                        nResultType = DEFECT_CRACK;
                    }
                }
                else
                {
                    nResultType = DEFECT_CRACK;
                }
            }
            else
            {
                nResultType = DEFECT_WEAK_POINT_D;
            }
        }
        else
        {
            if (f.peakMax > g_D_LINEAR_BASE_BRIGHT)
            {
                if (f.angleDeg < g_D_LINE_ANGLE_LOW || f.angleDeg > g_D_LINE_ANGLE_HIGH)
                {
                    nResultType = DEFECT_SCRATCH;
                }
                else
                {
                    nResultType = DEFECT_CRACK;
                }
            }
            else
            {
                nResultType = DEFECT_BLACK_WEAK;
            }
        }
    }
    else
    {
        if (!bIsLinear)
        {
            if (f.peakMax > g_D_WHITE_PEAK_IF)
            {
                nResultType = DEFECT_PINHOLE;
            }
            else if (f.peakMax > g_D_WHITE_PEAK_ELSEIF)
            {
                if (f.ratioMopol > g_D_WHITE_RATIO)
                {
                    nResultType = DEFECT_MICRO_SCRATCH;
                }
                else
                {
                    nResultType = DEFECT_DENT;
                }
            }
            else
            {
                nResultType = DEFECT_WHITE_WEAK;
            }
        }
        else
        {
            if (f.peakMax < g_D_WHITE_LINE_PEAK)
            {
                nResultType = DEFECT_LINE;
            }
            else
            {
                nResultType = DEFECT_MICRO_SCRATCH;
            }
        }
    }

    return nResultType;
}

// ============================================================
// InspectImage : CChain 기반 blob 검사 진입점 (AREA_MIN, NDIL_CNT 등 전역변수 사용)
// ============================================================
extern "C" INSPECT_API int InspectImage(
    const unsigned char* bgr32, int width, int height, int stride, int threshold,
    DefectResult* results, int maxResults)
{
    if (!bgr32 || !results || width <= 0 || height <= 0 || maxResults <= 0) return 0;

    std::unique_ptr<unsigned char[]> pGray(new unsigned char[width * height]);
    for (int y = 0; y < height; ++y)
    {
        const unsigned char* row = bgr32 + y * stride;
        for (int x = 0; x < width; ++x)
        {
            const unsigned char* px = row + x * 4;
            pGray[y * width + x] = static_cast<unsigned char>(
                (px[0] * 114 + px[1] * 587 + px[2] * 299) / 1000);
        }
    }

    long sum = 0;
    for (int i = 0; i < width * height; ++i) sum += pGray[i];
    double meanGlobal = static_cast<double>(sum) / (width * height);

    double darkCut = std::max<double>(0.0, meanGlobal - static_cast<double>(threshold));
    double whiteCut = std::min<double>(255.0, meanGlobal + static_cast<double>(threshold));

    std::unique_ptr<unsigned char[]> pBinDark(new unsigned char[width * height]);
    std::unique_ptr<unsigned char[]> pBinWhite(new unsigned char[width * height]);
    for (int i = 0; i < width * height; ++i)
    {
        pBinDark[i] = (pGray[i] < darkCut) ? 255 : 0;
        pBinWhite[i] = (pGray[i] > whiteCut) ? 255 : 0;
    }

    std::unique_ptr<CChain> pChain_B(new CChain(g_AREA_MIN, 100000));
    pChain_B->SetChainData(1, pBinDark.get(), 1, 1, 2, 100000, width, height);
    int nBlobCnt_B = pChain_B->FastChain(1, 1, width - 1, height - 1);

    std::unique_ptr<CChain> pChain_W(new CChain(g_AREA_MIN, 100000));
    pChain_W->SetChainData(1, pBinWhite.get(), 1, 1, 2, 100000, width, height);
    int nBlobCnt_W = pChain_W->FastChain(1, 1, width - 1, height - 1);

    std::unique_ptr<unsigned char[]> pOriBinary(new unsigned char[width * height]);
    std::unique_ptr<unsigned char[]> pMopol(new unsigned char[width * height]);
    memcpy(pOriBinary.get(), pBinWhite.get(), sizeof(unsigned char) * width * height);
    memcpy(pMopol.get(), pBinWhite.get(), sizeof(unsigned char) * width * height);

    for (int i = 0; i < g_NDIL_CNT; ++i)
    {
        Dilate_BinaryMini(pOriBinary.get(), pMopol.get(), width, height, width);
    }

    std::unique_ptr<CChain> pChain_mopol(new CChain(40, 100000));
    pChain_mopol->SetChainData(1, pMopol.get(), 1, 1, 2, 100000, width, height);
    int nBlobCnt_mopol = pChain_mopol->FastChain(1, 1, width - 1, height - 1);

    std::vector<BlobFeature> feats;

    for (int i = 0; i < nBlobCnt_B && (int)feats.size() < maxResults; ++i)
    {
        double dArea = pChain_B->Chain_Area(i);
        if (dArea < g_AREA_MIN) continue;

        int nx1 = pChain_B->FindMinX(i);
        int nx2 = pChain_B->FindMaxX(i);
        int ny1 = pChain_B->FindMinY(i);
        int ny2 = pChain_B->FindMaxY(i);

        double dW = std::max<double>(1.0, nx2 - nx1);
        double dH = std::max<double>(1.0, ny2 - ny1);

        BlobFeature f{};
        f.x = nx1; f.y = ny1; f.w = static_cast<int>(dW); f.h = static_cast<int>(dH);
        f.area = dArea;
        f.areaRatio = dArea / (dW * dH);
        f.circularity = pChain_B->FindCompactness(i);
        f.angleDeg = std::abs(pChain_B->FindAngle(i));

        double dMinTemp = 255.0, dMaxTemp = 0.0;
        for (int yy = ny1; yy <= ny2 && yy < height; ++yy)
        {
            for (int xx = nx1; xx <= nx2 && xx < width; ++xx)
            {
                double diff = std::abs(static_cast<double>(pGray[yy * width + xx]) - meanGlobal);
                dMinTemp = std::min<double>(dMinTemp, diff);
                dMaxTemp = std::max<double>(dMaxTemp, diff);
            }
        }
        f.peakMin = dMinTemp;
        f.peakMax = dMaxTemp;

        f.areaObjPercent = dArea / (static_cast<double>(width) * static_cast<double>(height));
        f.ratioMopol = 0.0;
        f.ratio = dH / dW;
        f.isDark = true;

        feats.push_back(f);
    }

    for (int i = 0; i < nBlobCnt_W && (int)feats.size() < maxResults; ++i)
    {
        double dArea = pChain_W->Chain_Area(i);
        if (dArea < g_AREA_MIN) continue;

        int nx1 = pChain_W->FindMinX(i);
        int nx2 = pChain_W->FindMaxX(i);
        int ny1 = pChain_W->FindMinY(i);
        int ny2 = pChain_W->FindMaxY(i);

        double dW = std::max<double>(1.0, nx2 - nx1);
        double dH = std::max<double>(1.0, ny2 - ny1);

        BlobFeature f{};
        f.x = nx1; f.y = ny1; f.w = static_cast<int>(dW); f.h = static_cast<int>(dH);
        f.area = dArea;
        f.areaRatio = dArea / (dW * dH);
        f.circularity = pChain_W->FindCompactness(i);
        f.angleDeg = std::abs(pChain_W->FindAngle(i));

        double dMinTemp = 255.0, dMaxTemp = 0.0;
        for (int yy = ny1; yy <= ny2 && yy < height; ++yy)
        {
            for (int xx = nx1; xx <= nx2 && xx < width; ++xx)
            {
                double diff = std::abs(static_cast<double>(pGray[yy * width + xx]) - meanGlobal);
                dMinTemp = std::min<double>(dMinTemp, diff);
                dMaxTemp = std::max<double>(dMaxTemp, diff);
            }
        }
        f.peakMin = dMinTemp;
        f.peakMax = dMaxTemp;

        f.areaObjPercent = dArea / (static_cast<double>(width) * static_cast<double>(height));

        double dSizeMaxMopol = 0.0;
        int    nManIdxMopol = 0;
        for (int m = 0; m < nBlobCnt_mopol; ++m)
        {
            double s = pChain_mopol->Chain_Area(m);
            if (dSizeMaxMopol < s) { dSizeMaxMopol = s; nManIdxMopol = m; }
        }
        if (nBlobCnt_mopol > 0)
        {
            int mx1 = pChain_mopol->FindMinX(nManIdxMopol);
            int mx2 = pChain_mopol->FindMaxX(nManIdxMopol);
            int my1 = pChain_mopol->FindMinY(nManIdxMopol);
            int my2 = pChain_mopol->FindMaxY(nManIdxMopol);
            double mW = std::max<double>(1.0, mx2 - mx1);
            double mH = std::max<double>(1.0, my2 - my1);
            f.ratioMopol = mH / mW;
        }
        else
        {
            f.ratioMopol = 0.0;
        }

        f.ratio = dH / dW;
        f.isDark = false;

        feats.push_back(f);
    }

    std::sort(feats.begin(), feats.end(), [](const BlobFeature& a, const BlobFeature& b) {
        return a.area > b.area;
        });

    int count = std::min<int>(maxResults, static_cast<int>(feats.size()));
    for (int i = 0; i < count; ++i)
    {
        const BlobFeature& f = feats[i];
        bool bIsLinear = IsLinearBlob(f);
        int  defectType = ClassifyDefectType(f, bIsLinear);

        results[i].x = f.x;
        results[i].y = f.y;
        results[i].width = f.w;
        results[i].height = f.h;
        results[i].area = f.area;
        results[i].mean = meanGlobal;
        results[i].aspectRatio = (f.h > 0) ? static_cast<double>(f.w) / f.h : 0.0;
        results[i].defectType = defectType;
        results[i].isDark = f.isDark ? 1 : 0;
        results[i].isLinear = bIsLinear ? 1 : 0;

        results[i].areaRatio = f.areaRatio;
        results[i].circularity = f.circularity;
        results[i].angleDeg = f.angleDeg;
        results[i].peakMax = f.peakMax;
        results[i].areaObjPercent = f.areaObjPercent;
        results[i].ratioMopol = f.ratioMopol;
    }

    return count;
}