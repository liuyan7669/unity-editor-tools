# Changelog

## [2.0.0] - 2026-11-12

### 破坏性变更

- `com.cowart.editor-tools` 改为 `com.cowart.editor-utilities`；更新业务项目的安装地址、程序集和源码引用。
- 完成包标识、目录、程序集、命名空间、脚本及文档改名，保持资源 GUID 和包独立性。

## [1.0.10] - 2026-11-08

- 统一 Cowart 包图标：重新设计为按功能类别配色的圆角图标（扳手），同步更新 `Icon.png` 与 `Icon@2x.png`。

## [1.0.9] - 2026-08-23

- 新增包专用图标资源。

## [1.0.8] - 2026-08-15

- 新增资源依赖与反向引用查找器。
- 新增场景、Prefab、ScriptableObject 丢失脚本和丢失引用扫描。
- 新增场景体检、Prefab Override 批量管理和材质批量替换工具。
- 新增选择历史、项目持久化收藏夹和资源信息复制工具。

## [1.0.6] - 2026-08-07

- 将组件菜单优先级归入 Cowart 基础物体工具组，与物体、碰撞体连续显示。
- 顶部菜单和物体右键菜单使用相同分组优先级。

## [1.0.5] - 2026-08-07

- 将组件复制、剪切、粘贴的顶部入口统一迁移到 `Cowart > 组件`。
- 将对应物体右键入口统一迁移到 `Cowart > 组件` 子菜单。

## [1.0.4] - 2026-08-07

- Added multi-object paste from the current Hierarchy selection for both menu entry points.
- Made the full batch a single Undo operation and deferred cut-source deletion until every target succeeds.
- Kept each target's internal references self-contained; cut external references are transferred to the active target.

## [1.0.3] - 2026-08-07

- Replaced the component transfer window with separate `Component > 复制 / 剪切 / 粘贴` commands.
- Added matching copy, cut, and paste commands to the Hierarchy GameObject context menu.
- Added Transform and RectTransform snapshots so their Inspector values are pasted with all other components.
- Changed cut to remove only non-Transform source components after a successful paste.

## [1.0.2] - 2026-08-07

- Added a component copy and cut window under `Cowart > 项目开发工具`.
- Added same-type component overwrite, missing-component creation, and serialized reference remapping.
- Added single-step Undo, failed-operation rollback, and loaded-scene reference updates for cut operations.

## [1.0.1] - 2026-07-31

- Renamed and grouped all editor tool commands under `Cowart > 项目开发工具`.

## [1.0.0] - 2026-07-31

- Initial UPM package.
- Added model export, runtime class debugging, source extraction, Text font scanning, and texture compression tools.
