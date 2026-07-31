# Editor Tools

面向 Unity 项目的纯编辑器工具包，提供资源检查、批处理、源码整理和运行时调试辅助功能。所有代码仅在 Unity Editor 中编译，不会进入 Player 构建。

## 功能

### 模型文件导出

- 扫描项目 `Assets` 目录中的模型资源
- 按名称和路径筛选模型
- 将选中的模型及其 `.meta` 文件复制到指定目录
- 导出前检查重名文件和目标覆盖风险

菜单入口：`Tools > Cowart > 模型文件导出`

### 运行时类调试器

- 在 Play Mode 中查找当前运行时对象
- 查看对象字段、属性和嵌套值
- 调用可用方法并填写参数
- 支持使用 JSON 输入复杂参数

菜单入口：`Tools > Cowart > 运行时类调试器`

### 源码提取工具

- 递归扫描指定目录中的源码文件
- 合并为一个 UTF-8 无 BOM 文本文件
- 按原目录结构复制源码并生成文件清单
- 可选择包含 Unity 当前注册的 Packages 源码
- 不修改或删除源文件

菜单入口：`Tools > Cowart > 源码提取工具`

### Text 字体检查

- 检查当前已加载场景和项目 Prefab
- 同时支持 `UnityEngine.UI.Text` 和 `TMP_Text`
- 查找字体缺失或字体不一致的文本组件
- 支持批量替换字体并记录场景 Undo

菜单入口：`Tools > Cowart > Text 字体检查`

### 贴图压缩

- 扫描并筛选项目贴图
- 显示尺寸、类型、压缩和内存占用信息
- 批量修改选中贴图的 Max Size
- 贴图资源变化后自动刷新已打开的窗口

菜单入口：`Tools > Cowart > 贴图压缩`

## 安装

### 通过 OpenUPM 安装

在 Unity 的 `Edit > Project Settings > Package Manager` 中添加：

```text
Name: package.openupm.com
URL: https://package.openupm.com
Scope: com.cowart.editor-tools
```

然后通过 `Add package by name` 安装：

```text
com.cowart.editor-tools
```

### 通过 Git URL 安装

```text
https://github.com/liuyan7669/unity-editor-tools.git#1.0.0
```

## 使用方法

所有工具统一位于 Unity 顶部菜单：

```text
Tools > Cowart
```

### 导出模型文件

1. 打开 `Tools > Cowart > 模型文件导出`。
2. 刷新模型列表，通过名称或路径筛选资源。
3. 勾选需要导出的模型并设置项目外的输出目录。
4. 根据需要选择是否同时导出 `.meta` 文件，然后执行导出。

工具会为每次导出创建独立目录，不会覆盖已有导出结果。为避免导出的模型被 Unity 再次导入，输出目录不能位于当前项目的 `Assets` 目录内。

### 调试运行时类

1. 进入 Play Mode。
2. 打开 `Tools > Cowart > 运行时类调试器`。
3. 刷新并搜索需要检查的运行时对象。
4. 打开对象详情查看字段、属性和嵌套值。
5. 需要调用方法时填写参数并执行；复杂参数可以使用 JSON 输入。

运行时方法调用可能直接改变当前运行状态，应优先在测试场景中使用。

### 提取项目源码

1. 打开 `Tools > Cowart > 源码提取工具`。
2. 选择源码目录和项目外的输出目录。
3. 配置扩展名、排除目录以及是否包含 `.meta` 和 Packages 源码。
4. 选择“合并为单个文本”或“按原目录结构复制”。
5. 确认扫描预览后执行提取。

默认扫描项目的 `Assets` 目录，默认输出到项目根目录下的 `SourceCodeExports`。工具只读取源文件，不会修改或删除源文件。

### 检查和替换 Text 字体

1. 打开 `Tools > Cowart > Text 字体检查`。
2. 刷新当前已加载场景和项目 Prefab 中的文本组件。
3. 使用类型、字体和关键字条件筛选结果。
4. 为需要处理的 `Text` 或 `TMP_Text` 指定新字体。
5. 单独应用或批量替换选中的字体。

场景修改会记录 Undo 并标记为未保存，不会自动保存场景；Prefab 修改会直接保存对应 Prefab 资源。

### 检查和修改贴图压缩

1. 打开 `Tools > Cowart > 贴图压缩`。
2. 刷新并筛选项目中的贴图资源。
3. 查看贴图尺寸、类型、压缩方式和内存占用。
4. 选择目标 Max Size 并应用到选中的贴图。

应用 Max Size 会修改 `TextureImporter` 并重新导入贴图，批量处理前应确认筛选范围和选中资源。

## 主要工具

| 类型 | 说明 |
| --- | --- |
| `ModelFileExporterWindow` | 扫描、筛选并导出模型源文件 |
| `RuntimeClassDebuggerWindow` | 查找和检查 Play Mode 中的运行时对象 |
| `SourceCodeExtractorWindow` | 合并或按目录结构复制项目源码 |
| `TextFontScannerWindow` | 检查并替换场景和 Prefab 中的 Text/TMP 字体 |
| `TextureCompressionWindow` | 检查贴图信息并批量修改 Max Size |

## 注意事项

- 本包全部代码只在 Unity Editor 中编译，不会进入 Player 构建。
- 模型导出和源码提取默认不会覆盖已有输出结果。
- Text 字体批量替换会修改场景或 Prefab，执行前应确认筛选范围。
- 贴图 Max Size 修改会触发资源重新导入，大批量处理可能需要一定时间。
- 运行时类调试器使用反射读取对象和调用方法，仅建议用于开发与测试。

## 环境要求

- Unity 2021.3 或更高版本
- Newtonsoft Json `3.2.2`
- TextMeshPro `3.0.6`
- Unity UI `1.0.0`

## 许可证

本包使用 MIT License，详情参见仓库中的许可证文件。
