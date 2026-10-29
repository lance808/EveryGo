# EveryGo

Windows 便携式桌面启动面板：默认只有一个「＋」，添加 exe / bat / cmd 后单击图标启动。单 exe、免安装、免管理员、不联网、无第三方 DLL。

当前状态：**Phase 1 骨架**（v0.1.0）。已完成空面板、工具栏、设置壳、配置读写、`--selftest`；添加/启动/自启/托盘/背景将在后续阶段接入。

## 构建

环境：Windows 11 x64 + .NET Framework 4.8.1 + VS 2022 Build Tools（Roslyn `csc.exe`）。

```powershell
.\build.ps1            # 只构建，产物在 dist\EveryGo.exe
.\build.ps1 -SelfTest  # 构建并运行自检，打印自检报告
```

构建脚本会自动定位 Roslyn `csc.exe`（优先 VS 2022 Build Tools），找不到时回退系统 `csc.exe`；都没有则明确报错。`dist\` 下只会有一个 exe。

## 运行

```powershell
.\dist\EveryGo.exe
```

- 默认无标题栏、圆角、深色磨砂卡片，默认置顶。
- 工具栏：左侧 6 点拖动手柄；右侧「置顶 / 设置 / 最小化 / 关闭」。
- 空状态只有居中的「＋」；点击「＋」在 Phase 1 仅提示，Phase 2 接入文件选择器。
- 拖动卡片空白处可移动窗口，位置会记住；位置越界会自动居中。
- 重复启动 exe 会聚焦已有窗口，不会新开第二个。

## 数据位置

优先 `exe 同目录\EveryGo.data\`（便携）；若该目录不可写，自动回退到 `%APPDATA%\EveryGo\`，实际路径可在设置窗口查看。

```text
EveryGo.data\
  config.json
  config.bak.json
  EveryGo.log
```

- 配置原子写入，保留上一版 `config.bak.json`。
- 配置损坏时改名为 `config.corrupt-<时间>.json`，以默认值启动并在面板内提示。
- `config.json` 已预留 `gridColumns`（4/5/6，默认 4）与 `consoleMode`（show/minimized/hidden，默认 minimized）字段，供后续阶段接入。

## 自检

```powershell
.\dist\EveryGo.exe --selftest
```

- 退出码 0 表示全部通过。
- 报告写入 `%TEMP%\EveryGo.selftest.log`。
- 自检不创建窗口、不写注册表、不修改真实配置（使用 `%TEMP%\EveryGo-selftest\` 临时目录）。

## 已知限制（Phase 1）

- 「＋」添加应用、图标提取与启动尚未接入（Phase 2）。
- 开机自启开关、关闭到托盘尚未接入（Phase 2）。
- 图片背景、模糊、色调、设置窗口完整控件尚未接入（Phase 3）。
- 托盘与真实桌面模糊在 Phase 2/3 实现；若系统模糊不可用或影响拖动，按规格回退到内部磨砂。
- 系统未开启 `LongPathsEnabled`，超过 260 字符的路径会给出明确提示而不是静默失败。
