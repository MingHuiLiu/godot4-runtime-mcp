# Runtime Black Box Scene Checkpoint and TSCN Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** 在 Godot 4 .NET 运行时无副作用地采集完整可观察场景节点树、持久化及离线查询 Checkpoint，并通过单独的 Godot 进程导出和验证原生 .tscn。

**Architecture:** Godot 主线程只读采样，转换为无引擎依赖的版本化 DTO，异步写入带校验的本地归档。RuntimeHost 负责离线查询和受控导出作业；新的 Godot Headless 进程从 DTO 创建临时节点树、PackedScene.Pack 并回读验证，绝不为打包修改 live 树。

**Tech Stack:** Godot 4.7 Mono / C#、.NET 8、System.Text.Json Source Generation、BCL GZip、现有 ModelContextProtocol.AspNetCore / System.CommandLine、macOS Native AOT。

**Spec:** docs/superpowers/specs/2026-10-20-runtime-black-box-scene-snapshot-design.md

## Global Constraints

- Godot 4 .NET/C# 是游戏端前提；不添加 Python、数据库、FFmpeg、常驻外部服务或自制 MCP 实现。
- 对 Godot.Object/Node/Resource 的访问只在引擎主线程；后台线程只能处理已复制的纯 DTO/字节。
- addon 唯一规范源码在 addons/godot_mcp/；ExampleProject 的插件快照及 AOT 嵌入资源均需同步。
- 默认根为 SceneTree.CurrentScene，缺失时如果没有 rootPath 则返回 scene_unavailable。默认排除 /root Autoload。
- 首版导出模式为 flatten + project_relative；没有任意时刻确定性回放，不自动恢复脚本私有状态。
- 初始限额：深度 32、单字段 256 KiB、节点 2000、资源 4096、单快照未压缩 DTO 64 MiB；限额错误不伪装成功。
- 禁止路径穿越/符号链接逃逸；输出只能写到授权的私有目录，不能静默覆盖项目的 res://。
- 默认排除密码/输入文本；不可表达的节点/值/资源必须产生结构化 Issue。strict=true 遇到关键丢失必须失败。
- 工具 CLI 命名沿用原仓库转换：godot_snapshot_capture 对应 godot-snapshot-capture，不承诺 snapshot-capture 短别名。
- Headless ExportWorker 只运行可信的本机项目；它是进程隔离而非安全沙箱，其他项目 Autoload 仍可能执行。

## Review Focus

最可能影响真实使用的五类输入；实施任务必须写出对应回归测试：
1. 同一路径的新老 Node 不被视作同一对象；Task 3。
2. 两节点引用同一 Resource 与 Resource 环引用不无限递归、保留共享关系；Task 4。
3. 未写完 manifest、校验不一致、未知 schema 均拒绝作为完成快照读取；Task 2。
4. snapshotId 为 ../escape、项目 ID 不匹配、录制目录软链接逃逸均拒绝；Task 6。
5. ExportWorker 不启动主游戏场景、不注册常规 MCP 实例；即使项目其他 Autoload 启动也明确标注限制；Task 8。

---

## File Map

- addons/godot_mcp/Recording/SnapshotContracts.cs：纯 C# SnapshotValue、RecordedNode、RecordedResource、SceneCheckpoint、Issue、Manifest、Job DTO。
- addons/godot_mcp/Recording/SnapshotJsonContext.cs：Source Generation，上述 DTO 在 Godot 与 RuntimeHost 双端共享。
- addons/godot_mcp/Recording/CheckpointArchive.cs：纯 BCL JSONL/GZip 写入、哈希校验和读取，双端共享。
- addons/godot_mcp/Recording/SceneCheckpointCapture.cs：当前树 DFS、节点身份和属性收集。
- addons/godot_mcp/Recording/VariantSnapshotCodec.cs：Godot Variant 到有类型 SnapshotValue，Resource 防环处理。
- addons/godot_mcp/Recording/CaptureJobController.cs：采集/写盘状态机，有界并发。
- addons/godot_mcp/Recording/SnapshotSceneBuilder.cs：隔离环境恢复临时节点树。
- addons/godot_mcp/Recording/ExportWorker.cs 和 export_worker.tscn：Headless 导出入口、PackedScene 与回读校验。
- RuntimeHost/Recording/CheckpointStore.cs：本机离线快照目录、身份验证和分页查询。
- RuntimeHost/Recording/ExportJobManager.cs：受控工作进程、超时与导出状态。
- RuntimeHost/ToolService.cs、RuntimeJsonContext.cs、Contracts.cs：宿主离线工具和序列化接口。
- addons/godot_mcp/GodotRuntimeBackend.cs 与 tool_manifest.json：运行时工具接入。
- Tests/Recording/：纯 .NET 测试；ExampleProject/scripts/SnapshotCaptureProbe.cs 与 scripts/test-snapshot-headless.sh：Godot 集成验证。
- scripts/sync-example-addon.sh、Tests/Program.cs、Tests/McpSdkSmoke.cs：递归同步与动态数量的工具测试。

**共享模型决策：** RuntimeHost.csproj 通过显式 Compile Include 编译 addon 目录下的 SnapshotContracts.cs、SnapshotJsonContext.cs、CheckpointArchive.cs；不得在 RuntimeHost 复制第二份 DTO。命名空间统一 GodotRuntimeMcp.Recording。宿主不得直接编译依赖 Godot 的文件。所有 Task 在同一隔离 worktree 中依序执行。

### Task 1: Shared Contracts and JSON Schema

**Files:** Create addons/godot_mcp/Recording/SnapshotContracts.cs, SnapshotJsonContext.cs, Tests/Recording/SnapshotContractTests.cs; Modify RuntimeHost/RuntimeHost.csproj, Tests/Program.cs。

**Interfaces:**
- SnapshotValue(string Type, JsonNode? Data, string? RefId)，Type 枚举为 null/bool/int64/float64/string/string_name/node_path/vector2/vector3/vector4/rect2/rect2i/color/quaternion/basis/transform2d/transform3d/aabb/packed_array/array/dict/node_ref/resource_ref。
- SnapshotIssue(string Code, string? NodeId, string? Property, string Message)。
- RecordedNode(string NodeId, string? ParentId, int SiblingIndex, string Name, string NativeClass, string? ScriptPath, string? SceneFilePath, string OriginalPath, string? OriginalOwnerId, bool IsInternal, bool UniqueNameInOwner, List<string> Groups, Dictionary<string,SnapshotValue> Properties, Dictionary<string,SnapshotValue> DiagnosticProperties, List<SnapshotIssue> Issues)。
- RecordedResource(string ResourceId, string ClassName, string? ResourcePath, string? Sha256, bool LocalToScene, Dictionary<string,SnapshotValue> Properties, List<SnapshotIssue> Issues)。
- SceneCheckpoint(int SchemaVersion, string SnapshotId, string SessionId, string ProjectId, string InstanceId, string RootNodeId, long StartedPhysicsFrame, long EndedPhysicsFrame, long StartedProcessFrame, long EndedProcessFrame, List<RecordedNode> Nodes, List<RecordedResource> Resources, Dictionary<string,SnapshotValue> RuntimeState, List<SnapshotIssue> Issues)。
- CheckpointManifest：SchemaVersion/上述 ID/状态/质量/各文件哈希/节点资源数量/UTC；SnapshotJobInfo：JobId、Status、SnapshotId、Code、Message。

- [ ] Step 1 — 写失败测试：SnapshotContractTests.Run(Action<bool,string> assert)，断言完整 nodeId、Owner、NodeRef、ResourceRef 的 System.Text.Json 往返不丢类型；在 Tests/Program.cs 调用。
- [ ] Step 2 — 运行 dotnet run --project Tests/Tests.csproj -c Release，预期因未定义 DTO 编译失败。
- [ ] Step 3 — 实现上述 records 与 SnapshotJsonContext；在 RuntimeHost.csproj 中引用同一来源文件。
- [ ] Step 4 — 重新运行 dotnet run --project Tests/Tests.csproj -c Release 和 dotnet build ExampleProject/ExampleProject.csproj；预期新的往返断言与编译通过。
- [ ] Step 5 — 提交：git add addons/godot_mcp/Recording RuntimeHost/RuntimeHost.csproj Tests && git commit -m "feat(snapshot): define shared checkpoint contracts"

### Task 2: Verified CheckpointArchive

**Files:** Create addons/godot_mcp/Recording/CheckpointArchive.cs, Tests/Recording/CheckpointArchiveTests.cs。

**Interfaces:** CheckpointArchive.CommitAsync(string snapshotDirectory,SceneCheckpoint checkpoint,CancellationToken ct) : Task<CheckpointManifest>；CheckpointArchive.ReadAsync(string snapshotDirectory,CancellationToken ct) : Task<SceneCheckpoint>；CheckpointArchive.ReadManifest(string snapshotDirectory) : CheckpointManifest。文件名固定 manifest.json、nodes.jsonl.gz、resources.jsonl.gz、state.json、issues.json、checksums.json。

- [ ] Step 1 — 写失败测试：CheckpointArchiveTests.RunAsync(...) 检查完全写/读、manifest 缺失、未知版本、文件被改、gzip 截断、仅部分文件写入，必须返回 snapshot_incomplete 或 snapshot_corrupt。
- [ ] Step 2 — 执行 dotnet run --project Tests/Tests.csproj -c Release；预期无 Archive API 而失败。
- [ ] Step 3 — 实现压缩 JSONL、哈希、最大读取量及原子临时文件提交；必须先提交所有数据文件，最后提交 manifest，禁止把临时快照认作完成。
- [ ] Step 4 — 重跑完整 .NET 测试；验证数据可读、损坏不可冒充完成快照、限制压缩炸弹。
- [ ] Step 5 — 提交：git add addons/godot_mcp/Recording/CheckpointArchive.cs Tests/Recording/CheckpointArchiveTests.cs && git commit -m "feat(snapshot): persist verified checkpoint archives"

### Task 3: Read-only Complete Node Topology Capture

**Files:** Create addons/godot_mcp/Recording/SceneCheckpointCapture.cs, ExampleProject/scripts/SnapshotCaptureProbe.cs, ExampleProject/SnapshotCaptureProbe.tscn, scripts/test-snapshot-headless.sh。

**Interfaces:** SceneCheckpointCapture.Capture(Node root,string snapshotId,string sessionId,string projectId,string instanceId,bool strict) : SceneCheckpoint。仅生成当前树拓扑（属性与资源留给 Task 4）。追踪原 GetInstanceId 到 nodeId；不按 NodePath 当身份。

- [ ] Step 1 — 写失败 Godot Probe：构造 Owner=null 的 Player/CollisionShape/Enemies、内部节点，以及先删除后同路径生成的新节点；记录采集前后 Owner、Parent、子节点排序及 TreeEntered/TreeExited 计数。
- [ ] Step 2 — 运行 GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot sh scripts/test-snapshot-headless.sh；预期 Capturer 缺失失败。脚本需要启动一个可退出的专用 Headless 测试场景。
- [ ] Step 3 — 实现只读 GetChildren(includeInternal:true) DFS、SiblingIndex、Group、OriginalOwnerId、IsInternal、NativeClass/ScriptPath、缺失 Issue；禁止调用 AddChild/RemoveChild/Owner setter 修改 live 树。
- [ ] Step 4 — 重跑 Godot Probe；预期 Owner-null 节点完整、事件计数不变、同路径新对象 nodeId 不复用。
- [ ] Step 5 — 提交：git add addons/godot_mcp/Recording/SceneCheckpointCapture.cs ExampleProject/scripts/SnapshotCaptureProbe.cs ExampleProject/SnapshotCaptureProbe.tscn scripts/test-snapshot-headless.sh && git commit -m "feat(snapshot): capture live scene topology safely"

### Task 4: Typed Variant and Resource Graph

**Files:** Create addons/godot_mcp/Recording/VariantSnapshotCodec.cs；Modify SceneCheckpointCapture.cs、ExampleProject/scripts/SnapshotCaptureProbe.cs；Test Tests/Recording/SnapshotValueTests.cs 及 Godot Probe。

**Interfaces:** VariantSnapshotCodec.Encode(Variant value,SnapshotEncodeContext context,string nodeId,string propertyName,int depth=0) : SnapshotValue?。SnapshotEncodeContext 维护 Resource/Object ID 映射、总字节、资源数量、Issue 列表。只有带 PROPERTY_USAGE_STORAGE 的属性可序列化；其他诊断属性仅白名单；敏感文本默认排除。

- [ ] Step 1 — 写失败测试：bool/int/string、Vector/Transform/Rect/Color、Array/Dictionary、NodeRef、两个对象共享 Shape、互相引用的 Resource、缺失外部文件/变更外部资源、未知 Variant、最大深度 32/单字段 256 KiB/2000 节点/4096 资源/64 MiB 超限。
- [ ] Step 2 — 运行 dotnet run --project Tests/Tests.csproj -c Release 与 sh scripts/test-snapshot-headless.sh；预期 Codec 不存在或属性测试失败。
- [ ] Step 3 — 实现白名单属性、Variant tagged data、稳定 refId、循环引用防护、外部 Resource hash 和限额/Issue；单独测试每种精确类型以避免 ToString 丢信息。
- [ ] Step 4 — 重跑两组测试；预期共享资源保留身份，未知属性绝不报告完整，超限给错误码。
- [ ] Step 5 — 提交：git add addons/godot_mcp/Recording Tests/Recording ExampleProject/scripts/SnapshotCaptureProbe.cs && git commit -m "feat(snapshot): encode typed properties and resources"

### Task 5: Asynchronous Capture Jobs and Runtime MCP Routing

**Files:** Create addons/godot_mcp/Recording/CaptureJobController.cs；Modify addons/godot_mcp/GodotRuntimeBackend.cs、tool_manifest.json；Test Tests/Recording/SnapshotToolTests.cs 和 Godot Probe。

**Interfaces:** CaptureJobController.Start(string? rootPath,bool strict,int maxNodes=2000) : SnapshotJobInfo；GetStatus(string jobId) : SnapshotJobInfo；Pump() : void。Runtime 工具 godot_snapshot_capture 和 godot_snapshot_job_status，控制调用立即返回 JobId；真实采集在引擎帧内处理，后台只持久化 DTO。

- [ ] Step 1 — 写失败测试：Catalog 包含两工具、Godot Dispatcher 有匹配分支；默认根无效返回 scene_unavailable；重入给 snapshot_busy；状态 pending→writing→completed/failed；写盘异常带 Code。
- [ ] Step 2 — 运行 dotnet run --project Tests/Tests.csproj -c Release 与 Headless Probe；预期新工具未注册/未实现。
- [ ] Step 3 — 实现 Job 状态机和 Callback/queue，必要时修改 Backend._Process；禁止同步 GZip 或长文件写入阻塞 Pump；上限事件不进入旧无限 _logs。
- [ ] Step 4 — 重新运行；确认工具在原 HTTP 15 秒界限内快速返回，实时多实例项目/实例选择不变。
- [ ] Step 5 — 提交：git add addons/godot_mcp Tests/Recording/SnapshotToolTests.cs ExampleProject/scripts/SnapshotCaptureProbe.cs && git commit -m "feat(snapshot): expose asynchronous capture tools"

### Task 6: Offline Host Store, Query and Path Security

**Files:** Create RuntimeHost/Recording/CheckpointStore.cs；Modify RuntimeHost/ToolService.cs、RuntimeJsonContext.cs、Contracts.cs；Test Tests/Recording/CheckpointStoreTests.cs。

**Interfaces:** SnapshotNodePage(int Total,int Offset,int Limit,List<RecordedNode> Items) 定义在 RuntimeHost/Contracts.cs。CheckpointStore.List(string projectPath,int limit=50) : IReadOnlyList<CheckpointManifest>；Info(string projectPath,string snapshotId) : CheckpointManifest；QueryNodes(string projectPath,string snapshotId,string? nodeId,string? pathPrefix,string? className,int offset,int limit) : SnapshotNodePage。ProjectId = SHA-256(normalized project path)；recording root 默认 RuntimeRegistry.ConfigRoot/recordings。新增离线工具 godot_snapshot_list、godot_snapshot_info、godot_snapshot_nodes。

- [ ] Step 1 — 写失败测试：离线列出/查询分页；两项目同名目录隔离；非法 ../ ID、符号链接、跨项目读取、损坏分段、超大 offset 均拒绝或返回规范空页。
- [ ] Step 2 — 运行 dotnet run --project Tests/Tests.csproj -c Release；预期接口缺失。
- [ ] Step 3 — 实现受控存储路径、同用户私有权限、ID 白名单、查询上限、纯 JSON 输出、SourceGen 登记及 Host 直接调用。
- [ ] Step 4 — 重跑 .NET 测试，并运行 ./scripts/godot-runtime-cli-dev.sh tools --search snapshot；预期 Host 离线查询不依赖存活实例。
- [ ] Step 5 — 提交：git add RuntimeHost Tests/Recording/CheckpointStoreTests.cs && git commit -m "feat(snapshot): inspect verified checkpoints offline"

### Task 7: Isolated Godot SceneBuilder with Flatten

**Files:** Create addons/godot_mcp/Recording/SnapshotSceneBuilder.cs；Modify ExampleProject/scripts/SnapshotCaptureProbe.cs；Test scripts/test-snapshot-headless.sh。

**Interfaces:** SnapshotSceneBuilder.Build(SceneCheckpoint checkpoint,bool strict) : SceneBuildResult；SceneBuildResult(Node Root,IReadOnlyList<SnapshotIssue> Issues)。先构造 nativeClass，再关联受信任脚本，解析资源图、创建全部节点并设置父子关系，最后写回可持久化属性、组和合法新 Owner。

- [ ] Step 1 — 写失败测试：Capture DTO 在另一棵临时树上恢复 Owner-null Enemy、CollisionShape/RectangleShape、共享 Resource、组和子节点顺序；缺资源及未知内部节点严格模式失败，宽松模式标 partial。
- [ ] Step 2 — 运行 sh scripts/test-snapshot-headless.sh；预期 Builder 未实现。
- [ ] Step 3 — 实现 Builder、反向 typed decode、Resource 共享缓存、flatten 所需 Owner；不可恢复属性产出 Issue，不在旧 live tree 执行。
- [ ] Step 4 — 重跑 Probe；断言两个树互不共享 Node、原树未变、导出候选树含受支持属性/节点。
- [ ] Step 5 — 提交：git add addons/godot_mcp/Recording/SnapshotSceneBuilder.cs ExampleProject/scripts/SnapshotCaptureProbe.cs && git commit -m "feat(snapshot): reconstruct isolated scene graphs"

### Task 8: Headless TSCN Export and Validation

**Files:** Create addons/godot_mcp/Recording/ExportWorker.cs、export_worker.tscn；Modify addons/godot_mcp/GodotRuntimeBackend.cs、scripts/test-snapshot-headless.sh；Test Headless Capture → Export round-trip。

**Interfaces:** ExportWorker.RunJobAsync(string jobPath) : Task<SceneExportResult>；SceneExportResult(Status,ScenePath,RuntimeStatePath,DependenciesPath,ReportPath,Quality,Issues)。独立进程设置 GODOT_MCP_EXPORT_WORKER=1 禁止 Backend 正常注册。输出 scene.tscn、runtime_state.json、dependencies.json、reconstruction_report.json。

- [ ] Step 1 — 写失败集成测试：受控 ExportWorker 只读取 Snapshot 数据，不打开主游戏场景；保存 .tscn 后 ResourceLoader.Load<PackedScene>() + Instantiate 再比对节点数、顺序、Owner、关键属性；严格失败不得返回 completed；验证 worker 未注册新游戏实例。
- [ ] Step 2 — 运行 GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot sh scripts/test-snapshot-headless.sh；预期 Worker 未实现。
- [ ] Step 3 — 实现头部参数 --snapshot-job、Build/Pack/Save/Reload/Compare/Report、明确退出码；声明其他 Autoload 仍可能运行；设置独立输出临时目录后原子提交。
- [ ] Step 4 — 重跑 Headless Probe 和 dotnet build ExampleProject/ExampleProject.csproj；预期真实 Godot 加载成功且严格校验差异为空。
- [ ] Step 5 — 提交：git add addons/godot_mcp/Recording/ExportWorker.cs addons/godot_mcp/Recording/export_worker.tscn addons/godot_mcp/GodotRuntimeBackend.cs scripts/test-snapshot-headless.sh && git commit -m "feat(snapshot): export verified native tscn"

### Task 9: RuntimeHost Export Jobs and Offline MCP Tools

**Files:** Create RuntimeHost/Recording/ExportJobManager.cs；Modify ToolService.cs、RuntimeJsonContext.cs、Contracts.cs；Test Tests/Recording/ExportJobManagerTests.cs。

**Interfaces:** SceneExportJobStatus(string JobId,string Status,string? ScenePath,string? ReportPath,string? Code,string? Message) 定义在 RuntimeHost/Contracts.cs。ExportJobManager.Start(string projectPath,string snapshotId,bool strict,string? godotBinary) : SnapshotJobInfo；Status(string projectPath,string jobId) : SceneExportJobStatus。工具 godot_snapshot_export_tscn、godot_snapshot_export_status。ProcessStartInfo.ArgumentList 管理 --headless、--path、--scene、--snapshot-job；用户选择 GODOT_BIN 或显式可信可执行路径；超时、并发与 stdout/stderr 有界。

- [ ] Step 1 — 写失败测试：缺少 Godot、非 Mono 可执行文件、工作进程失败、超时、并发超额、路径逃逸、坏 Snapshot 都输出稳定 Code；原游戏退出后仍可用已完成归档导出。
- [ ] Step 2 — 运行 dotnet run --project Tests/Tests.csproj -c Release；预期 ExportJobManager 未实现。
- [ ] Step 3 — 实现持久 Job 目录、子进程取消/超时/退出码、Host 两个离线工具与 AOT context；Start 立即返回 JobId，Status 返回可验证输出路径。
- [ ] Step 4 — 重跑 .NET + Headless end-to-end；预期 godot-snapshot-export-tscn 在无运行游戏时启动新 Godot 进程导出 .tscn。
- [ ] Step 5 — 提交：git add RuntimeHost Tests/Recording/ExportJobManagerTests.cs && git commit -m "feat(snapshot): manage offline export jobs"

### Task 10: Recursive Addon Sync, Regression, Docs and AOT

**Files:** Modify scripts/sync-example-addon.sh、Tests/Program.cs、Tests/McpSdkSmoke.cs、docs/architecture.md、docs/development.md、skills/godot-runtime/SKILL.md；sync generated files under ExampleProject/addons/godot_mcp/。

**Interfaces:** script sync 递归复制规范 addon（排除 *.uid、*.import、.godot 缓存），测试比较所有规范相对文件名与字节；CLI/MCP 工具数量动态从 Manifest + Host 管理列表推导，清除硬编码 46/51 假设。

- [ ] Step 1 — 写失败测试：在规范 addon 新增子目录源文件后旧同步逻辑/固定数量断言失败；CLI、MCP stdio/HTTP 均发现新工具；完整流程的 Owner-null 示例支持严格导出。
- [ ] Step 2 — 运行 dotnet run --project Tests/Tests.csproj -c Release；预期旧同步比较或魔数断言失败。
- [ ] Step 3 — 改造同步脚本、动态工具断言、示例安装和文档 Skill；不顺手重构无关工具。
- [ ] Step 4 — 执行并记录：dotnet build Godot-Mcp.sln -c Release；dotnet run --project Tests/Tests.csproj -c Release；sh scripts/sync-example-addon.sh；dotnet build ExampleProject/ExampleProject.csproj；GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot sh scripts/test-snapshot-headless.sh；使用与 HostVersion.cs 一致的合法发布标签调用 scripts/publish-release.sh TAG osx-arm64，然后运行原生 CLI tools --search snapshot。不能执行的项目如实标明，不作通过宣称。
- [ ] Step 5 — 提交：git add scripts Tests docs skills ExampleProject/addons/godot_mcp && git commit -m "test(snapshot): verify full pipeline and release packaging"

## Spec Coverage and Acceptance Review

- §5.1/5.2：Task 3；§5.3/5.4：Task 4；§5.5：Tasks 3/5；§6.1：Tasks 1/2/6；§6.2/6.3：Tasks 7–9；§7：Tasks 5/6/9/10；§8：Tasks 2/4/6/8/9；§9：Tasks 3–10。
- 全程保留快照 Issue/完整度及工作任务错误码；不把完整的 Runtime State 或未知节点转换为无损 .tscn。
- 必须完成一次可复现验收：运行 ExampleProject → 捕获 Owner=null GameWorld → 停止游戏 → Host 离线 list/info → Headless Godot 导出 .tscn → 再加载并与快照比对。strict 情况结果必须无未声明丢失。
- 若 Godot 内部节点/脚本构造/外部资源不能被可靠恢复：保留原始拓扑及具体 Issue，strict 失败；不得为了测试过关而将其静默忽略。
- 任务按 TDD 执行；每项写失败测试、确认失败、最小实现、确认通过、独立提交。真实测试输出才是完成声明依据。

## Execution Handoff

本计划为用户审查稿；经用户确认后开始实际产品代码修改。建议独立子代理逐任务实施并由另一个代理逐任务审查，特别检查跨引擎序列化和 Owner-null 完整性；如希望节省资源，可选择同会话 Native TDD 实施并在末尾进行完整代码审查。开始执行时应先使用 superpowers:using-git-worktrees 建立隔离工作树，保留当前设计/计划分支。
