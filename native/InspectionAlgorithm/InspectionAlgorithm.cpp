#define INSPECT_EXPORTS
#include <windows.h>          // 명시적으로 가장 먼저
#define NOMINMAX              // windows.h의 min/max 매크로가 std::min/max와 충돌하는 것 방지
#include "InspectionAlgorithm.h"
#include "Fchain.h"      // CChain 클래스 (doc5와 동일 헤더)
#include <vector>
#include <algorithm>
#include <cmath>
#include <cstring>

// ================= 다크 불량 판정 임계값 (D-Value) =================
static constexpr double D_FORM_MIN_AREA_RATIO = 0.25;  // B-면적비율 > [D] 형태 판단 최소면
static constexpr double D_ROUNDNESS = 0.60;  // 불량 진원도 > [D] 진원도
static constexpr double D_DARK_AREA_PERCENT = 0.05;  // 불량 면적% > [D] Dark면적%
static constexpr double D_LINEAR_BASE_BRIGHT = 40.0;  // 다크 피크치 > [D] 선형 불량 기준밝기
static constexpr double D_LINE_ANGLE_LOW = 10.0;  // 불량각도 < 10도
static constexpr double D_LINE_ANGLE_HIGH = 90.0;  // 불량각도 > 90도

// ================= 백 불량 판정 임계값 (D-Value) =================
static constexpr double D_WHITE_PEAK_IF = 60.0;  // W-Val1(핀홀) > 화이트 피크치 (IF)
static constexpr double D_WHITE_PEAK_ELSEIF = 40.0;  // W-Val1(핀홀) > 화이트 피크치 (Else if)
static constexpr double D_WHITE_RATIO = 1.50;  // dRatio_mopol > Ratio<White>
static constexpr double D_WHITE_LINE_PEAK = 50.0;  // Value(라인) < 화이트 피크치

// ================= 공통 =================
static constexpr double D_LINEARITY_RATIO = 3.0;   // 라인성 판단 세로:가로 비율
static constexpr int    AREA_MIN = 4;     // 최소 blob 면적
static constexpr int    NDIL_CNT = 2;      // 팽창 반복 횟수

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
    return f.ratio > D_LINEARITY_RATIO;
}

// ============================================================
// 로직트리 기반 최종 불량 타입 판정
// ============================================================
static int ClassifyDefectType(const BlobFeature& f, bool bIsLinear)
{
    int nResultType = DEFECT_UNKNOWN;

    if (f.isDark)
    {
        if (!bIsLinear)
        {
            // ---- 비선형 다크 ----
            if (f.areaRatio > D_FORM_MIN_AREA_RATIO)
            {
                // B-면적비율 > [D] 형태 판단 최소면 : TRUE
                if (f.circularity > D_ROUNDNESS)
                {
                    // 불량 진원도 > [D] 진원도 : TRUE
                    if (f.areaObjPercent > D_DARK_AREA_PERCENT)
                    {
                        nResultType = DEFECT_CRATER;      // 분화구
                    }
                    else
                    {
                        nResultType = DEFECT_CRACK;       // 크랙
                    }
                }
                else
                {
                    nResultType = DEFECT_CRACK;           // 크랙 (진원도 FALSE)
                }
            }
            else
            {
                nResultType = DEFECT_WEAK_POINT_D;        // 다크 약불량
            }
        }
        else
        {
            // ---- 선형 다크 ----
            if (f.peakMax > D_LINEAR_BASE_BRIGHT)
            {
                if (f.angleDeg < D_LINE_ANGLE_LOW || f.angleDeg > D_LINE_ANGLE_HIGH)
                {
                    nResultType = DEFECT_SCRATCH;         // 스크래치
                }
                else
                {
                    nResultType = DEFECT_CRACK;           // 크랙
                }
            }
            else
            {
                nResultType = DEFECT_BLACK_WEAK;          // 흑 약불량
            }
        }
    }
    else
    {
        if (!bIsLinear)
        {
            // ---- 비선형 백 ----
            if (f.peakMax > D_WHITE_PEAK_IF)
            {
                nResultType = DEFECT_PINHOLE;             // 핀홀 (IF)
            }
            else if (f.peakMax > D_WHITE_PEAK_ELSEIF)
            {
                // Else if
                if (f.ratioMopol > D_WHITE_RATIO)
                {
                    nResultType = DEFECT_MICRO_SCRATCH;   // 미세긁힘
                }
                else
                {
                    nResultType = DEFECT_DENT;            // 찍힘
                }
            }
            else
            {
                nResultType = DEFECT_WHITE_WEAK;          // else -> 화이트 약불량
            }
        }
        else
        {
            // ---- 선형 백 ----
            if (f.peakMax < D_WHITE_LINE_PEAK)
            {
                nResultType = DEFECT_LINE;                // 라인
            }
            else
            {
                nResultType = DEFECT_MICRO_SCRATCH;       // 미세긁힘
            }
        }
    }

    return nResultType;
}

// ============================================================
// InspectImage : CChain 기반 blob 검사 진입점
// ============================================================
extern "C" INSPECT_API int InspectImage(
    const unsigned char* bgr32, int width, int height, int stride, int threshold,
    DefectResult* results, int maxResults)
{
    if (!bgr32 || !results || width <= 0 || height <= 0 || maxResults <= 0) return 0;

    // ---- BGR32 -> Gray 변환 ----
    unsigned char* pGray = new unsigned char[width * height];
    for (int y = 0; y < height; ++y)
    {
        const unsigned char* row = bgr32 + y * stride;
        for (int x = 0; x < width; ++x)
        {
            const unsigned char* px = row + x * 4;
            pGray[y * width + x] = static_cast<unsigned char>(
                (px[0] * 114 + px[1] * 587 + px[2] * 299) / 1000); // B,G,R 가중 평균
        }
    }

    // ---- 전역 평균 산출 ----
    long sum = 0;
    for (int i = 0; i < width * height; ++i) sum += pGray[i];
    double meanGlobal = static_cast<double>(sum) / (width * height);

    double darkCut = std::max<double>(0.0, meanGlobal - static_cast<double>(threshold));
    double whiteCut = std::min<double>(255.0, meanGlobal + static_cast<double>(threshold));

    // ---- 다크/화이트 이진화 ----
    unsigned char* pBinDark = new unsigned char[width * height];
    unsigned char* pBinWhite = new unsigned char[width * height];
    for (int i = 0; i < width * height; ++i)
    {
        pBinDark[i] = (pGray[i] < darkCut) ? 255 : 0;
        pBinWhite[i] = (pGray[i] > whiteCut) ? 255 : 0;
    }

    // ---- CChain 으로 blob 추출 (다크) ----
    CChain* pChain_B = new CChain(AREA_MIN, 100000);
    pChain_B->SetChainData(1, pBinDark, 1, 1, 2, 100000, width, height);
    int nBlobCnt_B = pChain_B->FastChain(1, 1, width - 1, height - 1);

    // ---- CChain 으로 blob 추출 (화이트) ----
    CChain* pChain_W = new CChain(AREA_MIN, 100000);
    pChain_W->SetChainData(1, pBinWhite, 1, 1, 2, 100000, width, height);
    int nBlobCnt_W = pChain_W->FastChain(1, 1, width - 1, height - 1);

    // ---- 핀홀류 모폴로지(팽창) blob (dRatio_mopol 산출용, doc5와 동일 절차) ----
    unsigned char* pOriBinary = new unsigned char[width * height];
    unsigned char* pMopol = new unsigned char[width * height];
    memcpy(pOriBinary, pBinWhite, sizeof(unsigned char) * width * height);
    memcpy(pMopol, pBinWhite, sizeof(unsigned char) * width * height);

    // Dilate_BinaryMini 는 기존 doc5와 동일한 팽창 함수 사용 (프로젝트 공용 유틸)
    //for (int i = 0; i < NDIL_CNT; ++i)
    //{
    //    Dilate_BinaryMini(pOriBinary, pMopol, width, height, width);
    //}

    CChain* pChain_mopol = new CChain(40, 100000);
    pChain_mopol->SetChainData(1, pMopol, 1, 1, 2, 100000, width, height);
    int nBlobCnt_mopol = pChain_mopol->FastChain(1, 1, width - 1, height - 1);

    std::vector<BlobFeature> feats;

    // ============================================================
    // 다크 blob 특징 추출
    // ============================================================
    for (int i = 0; i < nBlobCnt_B && (int)feats.size() < maxResults; ++i)
    {
        double dArea = pChain_B->Chain_Area(i);
        if (dArea < AREA_MIN) continue;

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
        f.circularity = pChain_B->FindCompactness(i);   // 진원도
        f.angleDeg = std::abs(pChain_B->FindAngle(i));

        // 피크치 (선형 불량 밝기 판정용) : compMask 영역 min/max 편차
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
        f.ratioMopol = 0.0; // 다크쪽은 사용 안 함
        f.ratio = dH / dW;   // 세로:가로
        f.isDark = true;

        feats.push_back(f);
    }

    // ============================================================
    // 화이트 blob 특징 추출
    // ============================================================
    for (int i = 0; i < nBlobCnt_W && (int)feats.size() < maxResults; ++i)
    {
        double dArea = pChain_W->Chain_Area(i);
        if (dArea < AREA_MIN) continue;

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

        // dRatio_mopol : 가장 큰 모폴로지 blob 기준 (doc5 방식)
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

    // ---- 큰 순서로 정렬 후 결과 채우기 ----
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
    }

    delete pChain_B;
    delete pChain_W;
    delete pChain_mopol;
    delete[] pOriBinary;
    delete[] pMopol;
    delete[] pBinDark;
    delete[] pBinWhite;
    delete[] pGray;

    return count;
}