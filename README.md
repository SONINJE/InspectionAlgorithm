# InspectionLogicViewer

이미지에서 Dark/White defect를 검출하고, 판정에 사용된 로직 트리(Logic Tree)와 파라미터
여유값(Margin)을 함께 확인할 수 있는 WPF 뷰어입니다. 검출 로직은 C++ OpenCV DLL로 분리되어
있고, WPF UI가 이 DLL을 P/Invoke로 호출합니다.

## 구조

```
InspectionAlgorithm/
├── InspectionLogicViewer.sln          Visual Studio 솔루션 (두 프로젝트 묶음)
├── config/LogicTree.json              판정 로직 트리 정의 (조건 노드 그래프)
├── inspection_rules.json              검사 규칙 설정
├── native/InspectionAlgorithm/        C++ 네이티브 DLL (로직)
│   ├── InspectionAlgorithm.h          공개 API (InspectImage, Get/SetInspectParams)
│   ├── InspectionAlgorithm.cpp        OpenCV 기반 defect 검출 구현
│   ├── Fchain.h / Fchain.cpp
│   ├── InspectionAlgorithm.vcxproj
│   └── third_party/opencv/            OpenCV (include / x64/bin / x64/lib) — Git LFS로 포함
└── src/InspectionLogicViewer.Wpf/     WPF UI (View)
    ├── MainWindow.xaml(.cs)           이미지 뷰, 줌/팬, 결과 오버레이, 파라미터 패널
    ├── ParamDialog.xaml(.cs)          InspectParams 편집 다이얼로그
    ├── CropWindow.xaml(.cs)           이미지 크롭
    └── InspectionLogicViewer.Wpf.csproj
```

역할 분리:
- **로직(C++/OpenCV)**: 이미지에서 Dark/White defect 후보를 검출하고, 면적·둥근 정도·선형성 등을
  판정해 `DefectResult` 배열로 반환합니다. `extern "C"`로 export되어 C#에서 P/Invoke로 그대로
  호출합니다. 판정 임계값은 `InspectParams` 구조체로 런타임에 조회/설정할 수 있습니다.
- **View(WPF)**: 이미지를 열어 DLL에 넘기고, 검출 결과를 캔버스에 오버레이로 그립니다. 또한
  `config/LogicTree.json`의 조건 트리를 그대로 시각화해서, 각 defect가 어떤 조건 분기를 거쳐
  판정되었는지, 그리고 현재 파라미터 값이 임계값에서 얼마나 여유(Margin)가 있는지 함께 보여줍니다.

## 빌드 방법

Visual Studio 2022 (v143 toolset)로 `InspectionLogicViewer.sln`을 열면 두 프로젝트
(C++ DLL / WPF 앱)가 함께 로드됩니다. `native/InspectionAlgorithm/third_party/opencv`에
OpenCV가 이미 포함되어 있어서 별도 설치 없이 바로 빌드됩니다.

### 1. InspectionAlgorithm.dll (C++, x64)
- include: `native/InspectionAlgorithm/third_party/opencv/include`
- lib: `native/InspectionAlgorithm/third_party/opencv/x64/lib` (기본 `opencv_world490.lib`,
  Debug는 `opencv_world490d.lib`)
- 출력: 솔루션 루트의 `bin\$(Configuration)\`

### 2. InspectionLogicViewer.Wpf (WPF, .NET 8)
- 빌드 후 `bin\$(Configuration)\`에서 `InspectionAlgorithm.dll`과 `opencv_world*.dll`을
  자동으로 실행 폴더로 복사합니다(csproj의 `CopyNativeDlls` 타겟). `InspectionAlgorithm`
  프로젝트를 먼저 빌드한 뒤 WPF 프로젝트를 빌드/실행하세요.
- OpenCV 버전을 바꾸는 경우 `InspectionAlgorithm.vcxproj`의 `OpenCVLibName`(및 관련
  `opencv_world490*` 참조)을 함께 수정합니다.

## 사용법
- **이미지 열기**: png/jpg/bmp/tif 등 이미지를 열면 defect 검출이 수행되고 결과가 오버레이로
  표시됩니다.
- **Ctrl + 마우스 휠 / 드래그**: 이미지·로직 트리 뷰 확대·축소 및 패닝.
- **파라미터 패널 / 다이얼로그**: `InspectParams`의 각 임계값을 조회·수정하고, 현재 값이
  판정 경계에서 얼마나 여유가 있는지 위험/주의 구간으로 표시합니다.
- **로직 트리 뷰**: `config/LogicTree.json`의 조건 노드를 트리로 보여주며, 노드를 선택하면
  관련 파라미터가 파라미터 패널에서 하이라이트됩니다.

## OpenCV 배치
`native/InspectionAlgorithm/third_party/opencv` 아래에 실제 OpenCV 파일(include, x64/bin,
x64/lib)을 배치하세요. `x64/bin`, `x64/lib`의 `*.dll`, `*.lib`는 `.gitattributes`에 정의된
Git LFS로 관리됩니다.
