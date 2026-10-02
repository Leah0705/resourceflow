# ResourceFlow

一个运营方管理多个地点的资源预约系统。用户按日期、人数和可用时间预约会议室、工作室或其他有容量限制的空间；工作人员配置资源、处理预约和现场候补，并查看操作记录。

## 技术栈

| 层次 | 实现 |
| --- | --- |
| 用户端与管理端 | TypeScript、React Native、Expo Router，支持 Web 和原生应用构建 |
| API | C#、ASP.NET Core / .NET 10，Core / Infrastructure / API 分层 |
| 数据 | EF Core、SQLite、版本化数据库迁移 |
| 权限 | 管理员身份验证、角色权限、可限定范围的 API Key |
| 业务规则 | 时区、开放时间、容量、组合资源、时长规则、临时占位、候补 |
| 运维与质量 | Docker、Nginx、健康检查、审计记录、xUnit、Jest、Playwright |

## 使用流程

配置地点的时区、开放时间、预约时长和开始时间间隔，按分区管理资源并设置容量。用户选择日期与人数，查看全天可用时间，也可按地点本地时间的 AM / PM 筛选。选择时间后，界面创建五分钟占位，用户填写联系方式并确认预约。工作人员通过后台处理预约、取消、候补和使用状态。

空数据库默认创建 Central Workspace 和 Harbour Studio 两个地点，包含会议室、工作室和工位。可选的演示数据生成器（`scripts/demo_data.py`）覆盖跨时区、跨午夜营业、容量上限和组合资源等场景。

## 本地运行

需要 .NET 10 和 Node.js 24。

```sh
npm ci
npm ci --prefix resourceflow-frontend
npm run dev
```

开发 API 默认在 http://localhost:5062。前端 API 地址通过 `resourceflow-frontend/.env` 中的 `EXPO_PUBLIC_API_URL` 配置，模板见同目录 `.env.template`。开发账号在 `ResourceFlowApi/appsettings.Development.json` 中配置。

## Docker 运行

在完整项目目录中，根据 `.env.example` 创建 `.env`，设置 JWT 密钥、管理员账号和允许的前端来源，然后运行：

```sh
docker compose -f docker-compose.release.yml up -d --build
```

此配置构建当前代码的后端、前端和代理镜像。默认入口为本机 80 端口，可用 `HOST_PORT` 调整；数据库、媒体和密钥保存在独立卷中。默认应用名称为 ResourceFlow。

## 检查

```sh
dotnet test ResourceFlowApi.Tests/ResourceFlowApi.Tests.csproj
TZ=UTC npm test --prefix resourceflow-frontend -- --runInBand
cd resourceflow-frontend
npx tsc --noEmit
npx expo export --platform web
```

CLI 在 `resourceflow-cli`，通过 `npm ci`、`npm test` 构建和验证。本地命令名为 `resourceflow`，当前作为私有项目使用。

## 实现边界

面向单运营方、单应用实例。公开提交、后台创建和候补转预约共用进程内写入门闩，串行执行空位检查与新增预约；占位也保存在进程内。预约修改、恢复和延长沿用已有机制，多实例部署仍需要共享占位及数据库级冲突控制。

数据模型由地点（Venue）、分区（Section）、资源（Resource）和资源组（ResourceGroup）构成，资源有容量（Capacity），预约记录人数（PartySize）。预约时长由地点默认时长和按人数设置的时长规则决定。每个地点可上传一份指南 PDF。预约状态依次为 Booked、Arrived、InUse、Finished，或 NoShow。

`docs/` 提供技术文档，`CHANGELOG.md` 记录版本改动。后台外部链接需通过 `RESOURCEFLOW_CLI_PACKAGE_URL`、`RESOURCEFLOW_API_DOCS_URL` 和 `RESOURCEFLOW_REPOSITORY_URL` 配置，未配置时不显示。
