# ADR-0003：Host 与 Bridge 之间使用命名管道上的 JSON-RPC 2.0

| 项 | 内容 |
|---|---|
| 状态 | Accepted；其中"实例发现文件 + 一次性密钥"的握手设计已被 [ADR-0008](0008-data-locations-and-instance-discovery.md) 取代 |
| 日期 | 2026-10-12（提出）· 2026-10-13（接受） |
| 决策人 | 项目负责人 |
| 门的类型 | 双向门：协议有版本号，可以演进 |

## 背景与问题
Host（.NET 10）与 Bridge（EPLAN 进程内，net481）需要一个本机进程间通信方式。约束：
- Bridge 和 EPLAN 自带的程序集共存，**不能引入可能与 EPLAN 冲突的依赖**。EPLAN 2026 自带 `Newtonsoft.Json 13.0.3`、`Grpc.Core 2.46`、`Google.Protobuf 3.24`，没有 `System.Text.Json`、`System.Memory`。
- 只服务本机的当前用户，不应暴露网络端口。
- 需要支持结构化数据、错误码、进度通知、取消和协议版本协商。

## 备选方案
1. **TCP 上的 gRPC**（复用 EPLAN 自带的 Grpc.Core）
2. **命名管道 + StreamJsonRpc**（微软的 JSON-RPC 库）
3. **命名管道 + 自己实现的轻量 JSON-RPC 2.0**，序列化在 Bridge 端使用 EPLAN 自带的 Newtonsoft.Json
4. **Remote Client + 自定义 Action**（见 ADR-0002 方案 2）

## 决策
选择**方案 3**：
- **传输**：Windows 命名管道。管道名为 `EplanMcp.<pid>.<随机值>`，ACL 只允许当前用户，并开启 `PipeOptions.CurrentUserOnly`（Host 端）。
- **帧格式**：与 LSP 相同的 `Content-Length: N\r\n\r\n<UTF-8 JSON>`，实现简单，方便调试。
- **消息**：JSON-RPC 2.0。方法只有几个：`handshake`、`invoke`（携带操作名和参数，即操作信封）、`cancel`；外加通知 `progress`。操作的种类通过信封中的操作名扩展，而不是增加新的 RPC 方法，这样协议本身保持稳定。
- **握手**：交换协议版本（整数，Host 支持 N 和 N-1），并校验 Bridge 写在实例发现文件中的一次性密钥。
- **序列化**：Contracts 是纯 POCO，统一使用 camelCase。Host 端用 `System.Text.Json`，Bridge 端用 EPLAN 自带的 Newtonsoft.Json（编译时引用，运行时不分发）。两边的一致性由 Contracts 的往返测试保证。

## 后果
- **好的**：
  - Bridge 零第三方依赖，从根上避免与 EPLAN 的程序集冲突；
  - 不开网络端口，攻击面小；
  - 协议很小（几百行代码），完全可控，Small/Medium 测试容易覆盖。
- **代价**：
  - 要自己实现并测试帧解析、超时、断线重连；
  - 两种序列化器之间的细节（日期、枚举、null）必须靠测试保证一致。
- **后续行动**：
  - M1 spike S3：验证编译时引用 Newtonsoft.Json 13、运行时使用 EPLAN 自带版本的做法可行；
  - Contracts 的往返测试覆盖全部 DTO。

## 各方案利弊
### 方案 1：gRPC over TCP
- 优点：强类型契约，性能好。
- 缺点：EPLAN 自带的是已停止维护的 C-core 版本，版本被 EPLAN 锁定；需要开放 TCP 端口；生成的代码和 protobuf 版本都受 EPLAN 牵制。
### 方案 2：StreamJsonRpc
- 优点：成熟，功能完整。
- 缺点：依赖链很长（MessagePack、Nerdbank.Streams、Microsoft.VisualStudio.Threading、System.Memory 等），在 EPLAN 进程中存在版本冲突风险。
### 方案 4：Remote Client
- 缺点：见 ADR-0002 方案 2。
