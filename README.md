# EYE-200 VRRF 测量 GUI

[English README](./README_EN.md)

![EYE-200 VRRF software screenshot](./docs/assets/software-screenshot.png)

这是一个基于 .NET 8 WPF 的 EYE-200 精测探头 VRRF 测量程序。程序通过串口控制探头采集亮度波形，使用固化的 901 点 causal FIR 模型计算 `Weighted Data`，再按 0.150 s 尾随窗口计算 VRRF Trend，并提供CA410风格的实时图表、结果表格和 CSV 导出。

## 功能

- 串口实测：自动枚举本机串口，默认优先选择 `COM37`。
- 采样点数：支持 `64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768`。
- 测量模式：`Single`、`Continuous`、`Interval`。
- 测量进度：采集时弹出进度窗口，显示命令阶段和数据块读取进度。
- 图表显示：Original Data、Weighted Data、VRRF Trend 可独立开关。
- 图表交互：右键菜单支持仅 X 轴缩放、仅 Y 轴缩放、自适应、仅左右拖拽、仅上下拖拽。
- 表格操作：底部历史结果表支持像 Excel 一样选择单元格并复制到 Excel。
- 导出：
  - 当前逐点数据导出 CSV。
  - 历史结果表右键导出 CSV。
  - Range/汇总数据导出 CSV。
- 图标与发布：支持自定义图标，并可发布为 Windows x64 单文件 exe。

## 运行

开发环境运行：

```powershell
cd "C:\sorce\document_now\diy\compile projects\EYE-VRRF"
dotnet run --project .\EyeVrrf.App\EyeVrrf.App.csproj
```

已发布的单文件程序位于：

```text
publish\win-x64-single\EyeVrrf.App.exe
```

## 构建与测试

```powershell
dotnet build
dotnet test
```

如果 GUI 正在运行，`dotnet build` 可能因为 exe/dll 被占用而失败。关闭正在运行的 `EyeVrrf.App.exe` 后重新构建即可。

## 单文件发布

```powershell
dotnet publish .\EyeVrrf.App\EyeVrrf.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\publish\win-x64-single
```

发布完成后，输出目录中会生成单个 `EyeVrrf.App.exe`。

## 项目结构

```text
EyeVrrf.App      WPF GUI 程序
EyeVrrf.Core     串口采集、VRRF 计算、CSV 导出
EyeVrrf.Tests    xUnit 测试
```

## VRRF 计算说明

算法参考自 [cnbright/flk_calculator](https://github.com/cnbright/flk_calculator)，本项目将其中用于 EYE-200 VRRF 的 FIR 加权与 Trend 计算流程移植到 .NET 8。

- 采样时间：`Time [sec] = (index + 1) * interval / 3000`。
- 默认基础采样率：`3000 Hz`。
- FIR 模型：由现有 `V#1.csv` 训练得到，并固化在 `EyeVrrf.Core/VrrfFirModelData.cs`。
- Trend 公式：`(max(weighted_window) - min(weighted_window)) / average(weighted_window) * 100`。
- 默认 Trend 窗口：`0.150 s`。

注意：较小采样点数可以用于波形采集，但由于 FIR 和 Trend 窗口需要足够的数据长度，过短采样可能无法产生完整 VRRF 结果。

## 设备参数

- 默认串口：`COM37`
- 波特率：`115200`
- 串口格式：8N1
- 命令序列：`STR,0`、`WCS,...`、`FCS,2,0`、`MMS,2`、`MES,1`、`WDR,0`、`WDR,1..N`
