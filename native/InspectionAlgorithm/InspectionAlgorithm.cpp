#define INSPECT_EXPORTS
#include <windows.h>          // 명시적으로 가장 먼저
#define NOMINMAX              // windows.h의 min/max 매크로가 std::min/max와 충돌하는 것 방지
#include "InspectionAlgorithm.h"
#include "Fchain.h"      // CChain 클래스 (실제 로직과 동일한 클래스)
#include <opencv2/opencv.hpp>
#include <vector>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <memory>

// ============================================================
// 후보 검출(1단계) 관련 상수 — SetDefectInfo에는 없는, 이 뷰어 자체의 "후보 찾기" 단계에서만 쓰인다.
// 실제 운영 코드의 후보 검색(캔디데이트 서치) 알고리즘은 제공되지 않아 기존 방식을 그대로 둔다.
// ============================================================
static constexpr int kCandidateAreaMin = 4;
static constexpr int kCropMargin = 12;        // 후보 bbox 주변에 얼마나 여유를 두고 크롭할지
static constexpr int kMopologyDilateCount = 2; // SetDefectInfo 원본과 동일: 팽창 2회
static constexpr int kPeakSubRange = 4;        // SetDefectInfo 원본과 동일: 피크치 계산시 ±4 여유

// ============================================================
// 실제 로직(TREE_PARAM) 파라미터 — C#에서 SetRealTreeParams로 갱신한다.
// ============================================================
static RealTreeParams g_params = {
    /*TargetBright*/    128,
    /*Weight_W*/        1,
    /*Weight_B*/        1,
    /*BinaryW*/         30,
    /*BinaryB*/         30,
    /*BinaryNull*/      20,
    /*InsulBinaryW*/    30,
    /*InsulBinaryB*/    30,
    /*Ratio_W*/         3.0,
    /*Ratio_B*/         3.0,
    /*SizeY_W*/         5.0,
    /*SizeY_B*/         5.0,
    /*SizeX_B*/         3.0,
    /*ThPinhole*/       60.0,
    /*ThExtrude*/       40.0,
    /*ThWhiteLine*/     50.0,
    /*ThDarkDefectMin*/ 40.0,
    /*BlackMinArea*/    50.0,
    /*CompactnessB*/    0.60,
    /*PercentB*/        5.0,
    /*AngleLow*/        10.0,
    /*AngleHigh*/       80.0,
    /*ScaleX*/          1.0,
    /*ScaleY*/          1.0,
    /*UseGaussian*/     1
};

extern "C" INSPECT_API void SetRealTreeParams(const RealTreeParams* params)
{
    if (!params) return;
    g_params = *params;
}

extern "C" INSPECT_API void GetRealTreeParams(RealTreeParams* params)
{
    if (!params) return;
    *params = g_params;
}

// ============================================================
// 공용 헬퍼: 팽창(모폴로지) — 기존 파일에 있던 것과 동일한 3x3 팽창.
// ============================================================
static int Dilate_BinaryMini(const unsigned char* fmSour, unsigned char* fmDest, int nWidth, int nHeight)
{
    int nKernelSizeY = 3, nKernelSizeX = 3;
    int nOrgX = static_cast<int>(nKernelSizeX / 2.0 - 0.5);
    int nOrgY = static_cast<int>(nKernelSizeY / 2.0 - 0.5);

    std::memset(fmDest, 0, static_cast<size_t>(nWidth) * nHeight);

    for (int i = 0; i < nHeight - nKernelSizeY; i++) {
        for (int j = 0; j < nWidth - nKernelSizeX; j++) {
            bool found = false;
            for (int k = 0; k < nKernelSizeY && !found; k++) {
                for (int l = 0; l < nKernelSizeX && !found; l++) {
                    if (fmSour[(i + k) * nWidth + j + l]) found = true;
                }
            }
            fmDest[(i + nOrgY) * nWidth + j + nOrgX] = found ? 255 : 0;
        }
    }
    return 1;
}

// ============================================================
// 실제 로직(SetDefectInfo) 그대로 포팅: 프로젝션 기반 이진화.
// elecIdx는 SURF_INSUL / SURF_NULL / (그 외=코팅부) 값을 그대로 받는다.
// ============================================================
static void BinarizeProj(cv::Mat src, cv::Mat dst, int nWidth, int nHeight,
    int thUp, int thDn, int thNull, int* nProjY, int* nProjX, bool isWhiteDefect, int elecIdx)
{
    static const int DEFECT_LINE_VALUE = 250;
    static const int DEFECT_INSU_LINE_VALUE = 150;

    int nTh = 0;
    int nTmp = 0;

    if (isWhiteDefect)
    {
        for (int xIndex = 0; xIndex < nWidth; xIndex++)
        {
            nTh = nProjY[xIndex] + thUp;
            for (int yIndex = 0; yIndex < nHeight; yIndex++)
            {
                if (elecIdx == SURF_INSUL)
                {
                    if (nTh > DEFECT_INSU_LINE_VALUE)
                    {
                        int nThLine = nProjX[xIndex] + thUp;
                        nTmp = *src.ptr(yIndex, xIndex);
                        *dst.ptr(yIndex, xIndex) = (nTmp > nThLine) ? 255 : 0;
                    }
                    else
                    {
                        nTmp = *src.ptr(yIndex, xIndex);
                        *dst.ptr(yIndex, xIndex) = (nTmp > nTh) ? 255 : 0;
                    }
                }
                else if (nTh > DEFECT_LINE_VALUE)
                {
                    int nThLine = nProjX[xIndex] + thUp;
                    nTmp = *src.ptr(yIndex, xIndex);
                    *dst.ptr(yIndex, xIndex) = (nTmp > nThLine) ? 255 : 0;
                }
                else
                {
                    nTmp = *src.ptr(yIndex, xIndex);
                    *dst.ptr(yIndex, xIndex) = (nTmp > nTh) ? 255 : 0;
                }
            }
        }
    }
    else
    {
        if (elecIdx == SURF_NULL)
        {
            for (int xIndex = 0; xIndex < nWidth; xIndex++)
            {
                const int nDarkNullAve = 60;
                nTh = nProjY[xIndex] - thNull;
                if (nTh < nDarkNullAve)
                {
                    for (int yIndex = 0; yIndex < nHeight; yIndex++)
                    {
                        int nThCol = nProjX[xIndex] - thNull;
                        nTmp = *src.ptr(yIndex, xIndex);
                        *dst.ptr(yIndex, xIndex) = (nTmp < nThCol) ? 255 : 0;
                    }
                }
                else
                {
                    for (int yIndex = 0; yIndex < nHeight; yIndex++)
                    {
                        nTmp = *src.ptr(yIndex, xIndex);
                        *dst.ptr(yIndex, xIndex) = (nTmp < nTh) ? 255 : 0;
                    }
                }
            }
        }
        else
        {
            for (int xIndex = 0; xIndex < nWidth; xIndex++)
            {
                const int nDarkAve = 60;
                nTh = nProjY[xIndex] - thDn;
                if (nTh < nDarkAve)
                {
                    for (int yIndex = 0; yIndex < nHeight; yIndex++)
                    {
                        int nThCol = nProjX[xIndex] - thDn;
                        nTmp = *src.ptr(yIndex, xIndex);
                        *dst.ptr(yIndex, xIndex) = (nTmp < nThCol) ? 255 : 0;
                    }
                }
                else
                {
                    for (int yIndex = 0; yIndex < nHeight; yIndex++)
                    {
                        nTmp = *src.ptr(yIndex, xIndex);
                        *dst.ptr(yIndex, xIndex) = (nTmp < nTh) ? 255 : 0;
                    }
                }
            }
        }
    }
}

// 실제 로직 그대로 포팅: ROI 내 이진 영상에서 객체(255) 픽셀 비율(0~1)을 구한다.
struct RectI { int left, top, right, bottom; };

static double GetObjectArea(const unsigned char* fmBi, int nWidth, int /*nHeight*/, RectI rtIns)
{
    int nCnt_BG = 0, nCnt_OBJ = 0;
    for (int y = rtIns.top; y < rtIns.bottom; y++)
    {
        for (int x = rtIns.left; x < rtIns.right - 1; x++)
        {
            int nVal = fmBi[y * nWidth + x];
            if (nVal == 0) nCnt_BG++;
            else if (nVal == 255) nCnt_OBJ++;
        }
    }
    double dSum = static_cast<double>(nCnt_OBJ + nCnt_BG);
    return (dSum != 0) ? (nCnt_OBJ / dSum) : 0.0;
}

// 실제 로직 그대로 포팅: 3x3 가우시안 커널 생성.
static void generateGaussKernel(double* dKernel, int diameter)
{
    double sigma = diameter / 4.0;
    int mean = diameter / 2;
    double sum = 0.0;

    for (int x = 0; x < diameter; ++x) {
        for (int y = 0; y < diameter; ++y) {
            dKernel[y * diameter + x] = std::exp(-0.5 * (std::pow((x - mean) / sigma, 2.0) + std::pow((y - mean) / sigma, 2.0))) / (2 * PI * sigma * sigma);
            sum += dKernel[y * diameter + x];
        }
    }
    for (int i = 0; i < diameter * diameter; ++i) dKernel[i] /= sum;
}

// 실제 로직 그대로 포팅: 가우시안(가중합) 필터.
static int GaussianBlur(cv::Mat src, cv::Mat dst, int nWidth, int nHeight,
    double* pKernel, int nKernelSizeX, int nKernelSizeY)
{
    int nOrgX = static_cast<int>(nKernelSizeX / 2.0 - 0.5);
    int nOrgY = static_cast<int>(nKernelSizeY / 2.0 - 0.5);

    for (int y = 0; y < nHeight; y++)
        for (int x = 0; x < nWidth; x++)
            *dst.ptr(y, x) = *src.ptr(y, x); // 가장자리 등 미처리 영역은 원본 유지

    for (int i = nOrgY; i < nHeight - nKernelSizeY; i++)
    {
        for (int j = nOrgX; j < nWidth - nKernelSizeX; j++)
        {
            double sum = 0;
            for (int k = 0; k < nKernelSizeY; k++)
                for (int l = 0; l < nKernelSizeX; l++)
                    sum += (*src.ptr(i + k, j + l)) * pKernel[nKernelSizeX * k + l];

            if (sum > 255) sum = 255;
            *dst.ptr(i + nOrgY, j + nOrgX) = static_cast<unsigned char>(sum);
        }
    }
    return 1;
}

// ============================================================
// 실제 로직(ClassifyDefectType) 그대로 포팅.
// ============================================================
struct RealFeature
{
    bool isWhite;
    double ratio;         // 장축/단축 (항상 1 이상)
    double sizeX, sizeY;  // 실측 크기
    double compactness;
    double angleDeg;
    double peakValue;
    double area;
    double areaRatioWithinRoiPercent; // 0~100
    double mopologyRatio;
};

static int ClassifyReal(int surfaceType, bool isInsulGap, const RealFeature& f, const RealTreeParams& p)
{
    if (surfaceType == SURF_INSUL)
    {
        if (f.isWhite)
        {
            if (isInsulGap)
                return (f.ratio > p.Ratio_W) ? RDC_INSUL_GAP_LINE : RDC_INSUL_GAP_SPOT;
            else
                return (f.ratio > p.Ratio_W) ? RDC_INSUL_LINE : RDC_INSUL_PINHOLE;
        }
        return RDC_INSUL_ISLAND;
    }

    if (surfaceType == SURF_NULL)
    {
        // 흑불량만 존재. Ratio_B 초과면 주름, 그 외엔 Compactness_B와 무관하게 아일랜드(원본과 동일).
        if (f.ratio > p.Ratio_B) return RDC_NONE_COATING_WRINKLE;
        return RDC_ISLAND;
    }

    // 코팅부
    if (f.isWhite)
    {
        bool linear = (f.ratio > p.Ratio_W) && (f.sizeY > p.SizeY_W);
        if (linear)
            return (f.peakValue >= p.ThWhiteLine) ? RDC_LINE : RDC_SCRATCH_TINY;

        if (f.peakValue > p.ThPinhole) return RDC_PINHOLE;
        if (f.peakValue > p.ThExtrude)
            return (f.mopologyRatio < p.Ratio_W) ? RDC_PROTRUSION : RDC_SCRATCH_TINY;
        return RDC_WEAK_POINT_W;
    }
    else
    {
        bool linear = (f.ratio > p.Ratio_B) && (f.sizeY > p.SizeY_B);
        if (linear)
        {
            if (f.peakValue > p.ThDarkDefectMin)
                return (f.angleDeg <= p.AngleLow || f.angleDeg >= p.AngleHigh) ? RDC_SCRATCH : RDC_CRACK;
            return RDC_WEAK_POINT_D;
        }

        // 원본 코드 그대로: Area<=BlackMinArea면 CRATER의 UseJudge로 게이트된 WEAK_POINT_D.
        if (f.area <= p.BlackMinArea) return RDC_WEAK_POINT_D;
        // Compactness/AreaRatio% 미달로 결과 미지정이던 경로는 CRACK으로 처리(사용자 요청 반영).
        if (f.compactness <= p.CompactnessB) return RDC_CRACK;
        return (f.areaRatioWithinRoiPercent > p.PercentB) ? RDC_CRATER : RDC_CRACK;
    }
}

// ============================================================
// SetDefectInfo 그대로 포팅: 후보 하나(bbox + 이미 알고 있는 극성)를 다시 크롭해서
// 전처리 → (가우시안) → 프로젝션 → 이진화 → Chain 분석까지 다시 수행해 실측 특징값을 구한다.
// 실제 운영 코드는 후보의 극성(백/흑)을 이 단계에서 다시 비교해 정하지만, 이 뷰어의 1단계
// 후보 검출이 이미 극성별로 분리되어 있으므로 그 결과를 그대로 신뢰한다.
// ============================================================
static bool AnalyzeDefectFeatures(
    const unsigned char* fullGray, int fullW, int fullH,
    int candX, int candY, int candW, int candH, bool isWhiteCandidate,
    int surfaceType, bool isInsulGap, const RealTreeParams& params,
    DefectResult& out)
{
    int cx1 = std::max(0, candX - kCropMargin);
    int cy1 = std::max(0, candY - kCropMargin);
    int cx2 = std::min(fullW, candX + candW + kCropMargin);
    int cy2 = std::min(fullH, candY + candH + kCropMargin);
    int cropWidth = cx2 - cx1;
    int cropHeight = cy2 - cy1;
    if (cropWidth < 4 || cropHeight < 4) return false;

    cv::Mat ngCropImage(cropHeight, cropWidth, CV_8UC1);
    for (int y = 0; y < cropHeight; y++)
        std::memcpy(ngCropImage.ptr(y, 0), fullGray + (cy1 + y) * fullW + cx1, cropWidth);

    cv::Mat preprocessImage = ngCropImage.clone();

    // electrodeMean: [20,200) 범위 픽셀의 평균
    long acc = 0; unsigned int count = 0;
    for (int y = 0; y < cropHeight; y++)
        for (int x = 0; x < cropWidth; x++)
        {
            int v = *preprocessImage.ptr(y, x);
            if (v >= 20 && v < 200) { acc += v; count++; }
        }
    double electrodeMean = (count > 0) ? (static_cast<double>(acc) / count) : 128.0;

    // 표면 종류별 전처리
    if (surfaceType == SURF_INSUL)
    {
        for (int y = 0; y < cropHeight; y++)
            for (int x = 0; x < cropWidth; x++)
            {
                int v = *preprocessImage.ptr(y, x);
                if (isWhiteCandidate && electrodeMean > v)
                    *preprocessImage.ptr(y, x) = static_cast<unsigned char>(electrodeMean);
                else if (!isWhiteCandidate && electrodeMean < v)
                    *preprocessImage.ptr(y, x) = static_cast<unsigned char>(electrodeMean);
            }
    }
    else if (surfaceType == SURF_NULL)
    {
        // 원본 이미지 그대로 사용
    }
    else
    {
        for (int y = 0; y < cropHeight; y++)
            for (int x = 0; x < cropWidth; x++)
            {
                int v = *preprocessImage.ptr(y, x);
                int diff = static_cast<int>(electrodeMean) - v;
                int nTmp = (diff < 0)
                    ? params.TargetBright + (std::abs(diff) * params.Weight_W)
                    : params.TargetBright - (std::abs(diff) * params.Weight_B);
                nTmp = std::clamp(nTmp, 0, 255);
                *preprocessImage.ptr(y, x) = static_cast<unsigned char>(nTmp);
            }
    }

    cv::Mat inspectImage;
    if (params.UseGaussian)
    {
        double kernel[9];
        generateGaussKernel(kernel, 3);
        inspectImage = preprocessImage.clone();
        GaussianBlur(preprocessImage, inspectImage, cropWidth, cropHeight, kernel, 3, 3);
    }
    else
    {
        inspectImage = preprocessImage.clone();
    }

    std::vector<int> projX(cropWidth, 0), projY(cropHeight, 0);
    for (int x = 0; x < cropWidth; x++)
    {
        for (int y = 0; y < cropHeight; y++) projX[x] += *inspectImage.ptr(y, x);
        projX[x] /= cropWidth;
    }
    for (int y = 0; y < cropHeight; y++)
    {
        for (int x = 0; x < cropWidth; x++) projY[y] += *inspectImage.ptr(y, x);
        projY[y] /= cropHeight;
    }

    int nThW = (surfaceType == SURF_INSUL) ? params.InsulBinaryW : params.BinaryW;
    int nThB = (surfaceType == SURF_INSUL) ? params.InsulBinaryB : params.BinaryB;

    cv::Mat whiteBinaryImage = cv::Mat::zeros(cropHeight, cropWidth, CV_8UC1);
    cv::Mat darkBinaryImage = cv::Mat::zeros(cropHeight, cropWidth, CV_8UC1);
    BinarizeProj(inspectImage, whiteBinaryImage, cropWidth, cropHeight, nThW, nThB, params.BinaryNull, projY.data(), projX.data(), true, surfaceType);
    BinarizeProj(inspectImage, darkBinaryImage, cropWidth, cropHeight, nThW, nThB, params.BinaryNull, projY.data(), projX.data(), false, surfaceType);

    // 모폴로지(팽창) — 원본과 동일하게 항상 whiteBinaryImage 기준으로 계산한다.
    cv::Mat mopologyImage = cv::Mat::zeros(cropHeight, cropWidth, CV_8UC1);
    {
        cv::Mat src = whiteBinaryImage.clone();
        cv::Mat dst = mopologyImage.clone();
        for (int i = 0; i < kMopologyDilateCount; i++)
        {
            Dilate_BinaryMini(src.data, dst.data, cropWidth, cropHeight);
            src = dst.clone();
        }
        mopologyImage = dst;
    }

    CChain chainMopol(80, 100000);
    chainMopol.SetChainData(1, mopologyImage.data, 1, 1, 2, 100000, cropWidth, cropHeight);
    int blobCntMopol = chainMopol.FastChain(1, 1, cropWidth - 1, cropHeight - 1);

    double mopologyRatio = 0.0;
    if (blobCntMopol > 0)
    {
        double maxArea = 0.0; int maxIdx = 0;
        for (int i = 0; i < blobCntMopol; i++)
        {
            double a = chainMopol.Chain_Area(i);
            if (a > maxArea) { maxArea = a; maxIdx = i; }
        }
        int mx1 = chainMopol.FindMinX(maxIdx), mx2 = chainMopol.FindMaxX(maxIdx);
        int my1 = chainMopol.FindMinY(maxIdx), my2 = chainMopol.FindMaxY(maxIdx);
        double mDefectX = (mx2 - mx1) * params.ScaleX;
        double mDefectY = (my2 - my1) * params.ScaleY;
        if (mDefectX != 0) mopologyRatio = mDefectY / mDefectX;
    }

    // 후보의 알려진 극성에 해당하는 이진 영상에서 Chain 분석
    cv::Mat& chosenBinary = isWhiteCandidate ? whiteBinaryImage : darkBinaryImage;
    CChain chain(80, 100000);
    chain.SetChainData(1, chosenBinary.data, 1, 1, 2, 100000, cropWidth, cropHeight);
    int blobCnt = chain.FastChain(1, 1, cropWidth - 1, cropHeight - 1);
    if (blobCnt <= 0) return false;

    double maxArea = 0.0; int maxIdx = 0;
    for (int i = 0; i < blobCnt; i++)
    {
        double a = chain.Chain_Area(i);
        if (a > maxArea) { maxArea = a; maxIdx = i; }
    }

    int nx1 = chain.FindMinX(maxIdx), nx2 = chain.FindMaxX(maxIdx);
    int ny1 = chain.FindMinY(maxIdx), ny2 = chain.FindMaxY(maxIdx);

    double compactness = chain.FindCompactness(maxIdx);
    double angleDeg = std::abs(chain.FindAngle(maxIdx));

    double sizeX = (nx2 - nx1) * params.ScaleX;
    double sizeY = (ny2 - ny1) * params.ScaleY;
    double ratio = 1.0;
    if (sizeX != 0 && sizeY != 0)
        ratio = (sizeY / sizeX <= sizeX / sizeY) ? (sizeX / sizeY) : (sizeY / sizeX);

    double centerX = 0, centerY = 0;
    chain.Chain_Center(maxIdx, &centerX, &centerY);
    double lenLong = 0, lenShort = 0, lenAvg = 0;
    chain.FineDistFromPoint(maxIdx, centerX, centerY, &lenLong, &lenShort, &lenAvg);

    // 대각선 보정 (30~60도 사이일 때 실측 길이 재계산)
    if (angleDeg >= 30 && angleDeg <= 60)
    {
        double maxPixel = lenLong * 2, minPixel = lenShort * 2;
        double realMax = std::sqrt(std::pow(maxPixel * std::cos(angleDeg * PI / 180) * params.ScaleX, 2) + std::pow(maxPixel * std::sin(angleDeg * PI / 180) * params.ScaleY, 2));
        double realMin = std::sqrt(std::pow(minPixel * std::cos((90 - angleDeg) * PI / 180) * params.ScaleX, 2) + std::pow(minPixel * std::sin((90 - angleDeg) * PI / 180) * params.ScaleY, 2));
        if (realMin != 0)
        {
            sizeX = realMin;
            sizeY = realMax;
            ratio = realMax / realMin;
        }
    }

    // 피크치: bbox ±4 픽셀 범위에서 electrodeMean 대비 최대 편차(원본 crop 기준)
    int sx1 = std::max(0, nx1 - kPeakSubRange), sx2 = std::min(cropWidth, nx2 + kPeakSubRange);
    int sy1 = std::max(0, ny1 - kPeakSubRange), sy2 = std::min(cropHeight, ny2 + kPeakSubRange);
    int maxTemp = 0, minTemp = 255;
    for (int y = sy1; y < sy2; y++)
        for (int x = sx1; x < sx2; x++)
        {
            int diff = static_cast<int>(*ngCropImage.ptr(y, x)) - static_cast<int>(electrodeMean);
            if (diff > maxTemp) maxTemp = diff;
            if (diff < minTemp) minTemp = diff;
        }
    if (minTemp == 255) minTemp = 0;
    double peakValue = isWhiteCandidate ? std::abs(maxTemp) : std::abs(minTemp);

    RectI roi{ nx1, ny1, nx2, ny2 };
    double areaObj = GetObjectArea(chosenBinary.data, cropWidth, cropHeight, roi);
    double areaRatioWithinRoiPercent = (areaObj > 0) ? (areaObj * 100.0) : 9999.0;

    RealFeature f{};
    f.isWhite = isWhiteCandidate;
    f.ratio = ratio;
    f.sizeX = sizeX;
    f.sizeY = sizeY;
    f.compactness = compactness;
    f.angleDeg = angleDeg;
    f.peakValue = peakValue;
    f.area = maxArea;
    f.areaRatioWithinRoiPercent = areaRatioWithinRoiPercent;
    f.mopologyRatio = mopologyRatio;

    int code = ClassifyReal(surfaceType, isInsulGap, f, params);

    out.x = cx1 + nx1;
    out.y = cy1 + ny1;
    out.width = std::max(1, nx2 - nx1);
    out.height = std::max(1, ny2 - ny1);
    out.area = maxArea;
    out.mean = electrodeMean;
    out.aspectRatio = (out.height > 0) ? static_cast<double>(out.width) / out.height : 0.0;
    out.defectType = code;
    out.isDark = isWhiteCandidate ? 0 : 1;
    out.isLinear = (surfaceType == SURF_COATING)
        ? (isWhiteCandidate ? (ratio > params.Ratio_W && sizeY > params.SizeY_W) : (ratio > params.Ratio_B && sizeY > params.SizeY_B))
        : false;

    out.areaRatio = 0.0; // 사용 안 함(구 로직 잔재) — 참고용으로 0 유지
    out.circularity = compactness;
    out.angleDeg = angleDeg;
    out.peakMax = peakValue;
    out.areaObjPercent = areaRatioWithinRoiPercent / 100.0; // C# 쪽은 0~1 비율을 기대함
    out.ratioMopol = mopologyRatio;

    out.ratio = ratio;
    out.sizeX = sizeX;
    out.sizeY = sizeY;

    return true;
}

// ============================================================
// 후보 검출(1단계, 기존 방식 유지) — 전역 평균±threshold로 다크/화이트 블롭을 찾는다.
// ============================================================
struct Candidate { int x, y, w, h; double area; bool isWhite; };

extern "C" INSPECT_API int InspectImage(
    const unsigned char* bgr32, int width, int height, int stride, int threshold,
    int surfaceType, int isInsulGap,
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

    CChain chainB(kCandidateAreaMin, 100000);
    chainB.SetChainData(1, pBinDark.get(), 1, 1, 2, 100000, width, height);
    int blobCntB = chainB.FastChain(1, 1, width - 1, height - 1);

    CChain chainW(kCandidateAreaMin, 100000);
    chainW.SetChainData(1, pBinWhite.get(), 1, 1, 2, 100000, width, height);
    int blobCntW = chainW.FastChain(1, 1, width - 1, height - 1);

    std::vector<Candidate> candidates;
    for (int i = 0; i < blobCntB; ++i)
    {
        double a = chainB.Chain_Area(i);
        if (a < kCandidateAreaMin) continue;
        int x1 = chainB.FindMinX(i), x2 = chainB.FindMaxX(i);
        int y1 = chainB.FindMinY(i), y2 = chainB.FindMaxY(i);
        candidates.push_back({ x1, y1, std::max(1, x2 - x1), std::max(1, y2 - y1), a, false });
    }
    for (int i = 0; i < blobCntW; ++i)
    {
        double a = chainW.Chain_Area(i);
        if (a < kCandidateAreaMin) continue;
        int x1 = chainW.FindMinX(i), x2 = chainW.FindMaxX(i);
        int y1 = chainW.FindMinY(i), y2 = chainW.FindMaxY(i);
        candidates.push_back({ x1, y1, std::max(1, x2 - x1), std::max(1, y2 - y1), a, true });
    }

    std::sort(candidates.begin(), candidates.end(), [](const Candidate& a, const Candidate& b) {
        return a.area > b.area;
        });

    RealTreeParams params = g_params;

    int count = 0;
    for (size_t i = 0; i < candidates.size() && count < maxResults; ++i)
    {
        const Candidate& c = candidates[i];
        DefectResult out{};
        if (AnalyzeDefectFeatures(pGray.get(), width, height, c.x, c.y, c.w, c.h, c.isWhite,
            surfaceType, isInsulGap != 0, params, out))
        {
            results[count++] = out;
        }
    }

    return count;
}
