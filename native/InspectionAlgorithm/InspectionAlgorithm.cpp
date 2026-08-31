#include "InspectionAlgorithm.h"
#include <opencv2/opencv.hpp>
#include <vector>
#include <algorithm>
#include <cmath>

// Tunable thresholds (값은 실험적으로 조정하세요)
static constexpr int AREA_MIN = 4;
static constexpr double AREA_RATIO_MIN = 0.25;
static constexpr double CRATER_CENTROID_INTENSITY = 50;
static constexpr double DARK_AREA_PERCENT_MIN = 0.05;
static constexpr double LINE_BRIGHTNESS_THRESHOLD = 200;
static constexpr double WHITE_PEAK_THRESHOLD = 200;
static constexpr double WHITE_RATIO_THRESHOLD = 0.15;
static constexpr double LINE_ANGLE_THRESHOLD_DEG = 10.0;

extern "C" INSPECT_API int InspectImage(
    const unsigned char* bgr32,int width,int height,int stride,int threshold,
    DefectResult* results,int maxResults)
{
    if(!bgr32 || !results || width<=0 || height<=0 || maxResults<=0) return 0;

    cv::Mat bgra(height,width,CV_8UC4,const_cast<unsigned char*>(bgr32),stride);
    cv::Mat gray,blurred;
    cv::cvtColor(bgra,gray,cv::COLOR_BGRA2GRAY);
    cv::GaussianBlur(gray,blurred,cv::Size(3,3),0);

    double meanGlobal=cv::mean(blurred)[0];
    double cutoff=std::max<double>(0.0, meanGlobal - static_cast<double>(threshold));

    cv::Mat maskDark;
    cv::compare(blurred,cutoff,maskDark,cv::CMP_LT);

    cv::Mat kernel=cv::getStructuringElement(cv::MORPH_RECT,cv::Size(3,3));
    cv::morphologyEx(maskDark,maskDark,cv::MORPH_OPEN,kernel);
    cv::morphologyEx(maskDark,maskDark,cv::MORPH_CLOSE,kernel);

    cv::Mat maskWhite;
    double whiteCut = std::min<double>(255.0, meanGlobal + static_cast<double>(threshold));
    cv::compare(blurred, whiteCut, maskWhite, cv::CMP_GT);
    cv::morphologyEx(maskWhite,maskWhite,cv::MORPH_OPEN,kernel);
    cv::morphologyEx(maskWhite,maskWhite,cv::MORPH_CLOSE,kernel);

    cv::Mat labels,stats,centroids;
    int labelCount=cv::connectedComponentsWithStats(maskDark,labels,stats,centroids,8,CV_32S);

    struct C { int x,y,w,h,area; double mean; cv::Rect bbox; cv::RotatedRect rrect; double angleDeg; double areaRatio; int darkOrWhite; double maxVal; double minVal; };
    std::vector<C> comps;

    for(int label=1;label<labelCount;++label) {
        int area=stats.at<int>(label,cv::CC_STAT_AREA);
        if(area<AREA_MIN) continue;

        cv::Rect bbox(
            stats.at<int>(label,cv::CC_STAT_LEFT),
            stats.at<int>(label,cv::CC_STAT_TOP),
            stats.at<int>(label,cv::CC_STAT_WIDTH),
            stats.at<int>(label,cv::CC_STAT_HEIGHT)
        );

        cv::Mat compMask;
        cv::compare(labels, label, compMask, cv::CMP_EQ);

        std::vector<std::vector<cv::Point>> contours;
        cv::findContours(compMask, contours, cv::RETR_EXTERNAL, cv::CHAIN_APPROX_SIMPLE);
        cv::RotatedRect rrect;
        double angleDeg = 0.0;
        if(!contours.empty()) {
            rrect = cv::minAreaRect(contours[0]);
            const cv::Size2f& s = rrect.size;
            double w = std::max<double>(1.0, static_cast<double>(s.width));
            double h = std::max<double>(1.0, static_cast<double>(s.height));
            angleDeg = rrect.angle;
            if(w < h) angleDeg = angleDeg + 90.0;
        } else {
            rrect = cv::RotatedRect(cv::Point2f(bbox.x + bbox.width*0.5f, bbox.y + bbox.height*0.5f),
                                    cv::Size2f((float)bbox.width,(float)bbox.height), 0.0f);
        }

        double boundArea = std::max<double>(1.0, static_cast<double>(bbox.width) * static_cast<double>(bbox.height));
        double areaRatio = static_cast<double>(area) / boundArea;

        double meanVal = cv::mean(gray, compMask)[0];
        double minVal, maxVal;
        cv::minMaxLoc(gray, &minVal, &maxVal, nullptr, nullptr, compMask);

        int darkOrWhite = 1;
        cv::Mat overlap;
        cv::bitwise_and(compMask, maskWhite, overlap);
        double overlapArea = cv::countNonZero(overlap);
        if(overlapArea > 0 && maxVal > WHITE_PEAK_THRESHOLD) darkOrWhite = 2;

        C c{
            bbox.x, bbox.y, bbox.width, bbox.height, area, meanVal, bbox, rrect, angleDeg, areaRatio, darkOrWhite, maxVal, minVal
        };
        comps.push_back(c);
    }

    std::sort(comps.begin(),comps.end(),[](const C&a,const C&b){return a.area>b.area;});
    int count=std::min<int>(maxResults, static_cast<int>(comps.size()));

    for(int i=0;i<count;++i) {
        const C& c=comps[i];
        double aspect = c.h>0 ? static_cast<double>(c.w)/c.h : 0.0;

        double maxSide = std::max<double>(static_cast<double>(c.rrect.size.width), static_cast<double>(c.rrect.size.height));
        double minSide = std::min<double>(std::max<double>(1.0, static_cast<double>(c.rrect.size.width)), std::max<double>(1.0, static_cast<double>(c.rrect.size.height)));
        bool isLinear = (maxSide / minSide) > 3.0;

        results[i].x=c.x; results[i].y=c.y; results[i].width=c.w; results[i].height=c.h;
        results[i].area=c.area; results[i].mean=c.mean; results[i].aspectRatio=aspect;

        int defectType = 0;
        if(c.darkOrWhite==1) {
            if(!isLinear) {
                if(c.areaRatio > AREA_RATIO_MIN) {
                    double defectCore = c.minVal;
                    if(defectCore < CRATER_CENTROID_INTENSITY) {
                        double areaPercent = static_cast<double>(c.area) / (static_cast<double>(width) * static_cast<double>(height));
                        defectType = (areaPercent > DARK_AREA_PERCENT_MIN) ? 10 : 11;
                    } else defectType = 11;
                } else defectType = 12;
            } else {
                if(c.maxVal > LINE_BRIGHTNESS_THRESHOLD) {
                    double ang = std::fmod(std::abs(c.angleDeg), 180.0);
                    bool nearVH = (ang < LINE_ANGLE_THRESHOLD_DEG) || (ang > 90.0 - LINE_ANGLE_THRESHOLD_DEG && ang < 90.0 + LINE_ANGLE_THRESHOLD_DEG);
                    defectType = nearVH ? 1 : 11;
                } else defectType = 13;
            }
        } else {
            if(!isLinear) {
                if(c.maxVal > WHITE_PEAK_THRESHOLD) defectType = 20;
                else if(c.maxVal > (WHITE_PEAK_THRESHOLD * 0.8)) {
                    defectType = (c.areaRatio > WHITE_RATIO_THRESHOLD) ? 21 : 22;
                } else defectType = 23;
            } else {
                defectType = (c.maxVal < WHITE_PEAK_THRESHOLD) ? 30 : 21;
            }
        }

        results[i].defectType = defectType;
        results[i].isDark = (c.darkOrWhite==1) ? 1 : 0;
        results[i].isLinear = isLinear ? 1 : 0;
    }
    return count;
}