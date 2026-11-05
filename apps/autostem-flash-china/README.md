# Skeer autoSTEM Flash China

**独立版本** - 基于 Skeer autoSTEM 后端的 Traktor Pro Web DJ 克隆

## 概述

`autostem-flash-china` 是一个独立的 Traktor Pro Web DJ 克隆项目，完全集成 Skeer autoSTEM 后端。与 `traktor-clone` 不同，这是一个**生产就绪版本**，所有分析数据都来自真实的后端 CLI。

## 架构

```
┌─────────────────────────────────────────────────────────────┐
│                    Browser (SPA)                            │
│                                                             │
│  Console2D (Lit WC)    ──►  SpatialXRBooth (Three.js)      │
│  ├── Track Library UI   │     └── 3D turntables             │
│  ├── Deck A/B/C/D UI   │     └── Waveform panels           │
│  ├── Mixer Controls    │     └── Club lighting             │
│  ├── Copilot ChatDrawer│                                  │
│  └── Gig Drawer        │                                   │
│       ▲                    ▲                                │
│       │ HTTP               │ AudioContext                   │
│       ▼                    ▼                                │
│  BackendBridge         AudioEngine                          │
│  (fetch → /api/*)      (Web Audio graph, 4 channels)        │
│       │                    │                                 │
│  StemService          BPM/Key/Waveform                      │
│  (real backend)       estimation (real backend)             │
│                                            ┌──────────────┐│
│                                    ┌───────│ autostem.exe ││
│                                    │       │  (real CLI)   ││
│                                    │       └───────────────┘│
│                                    │       ┌──────────────┐│
│                                    └───────│ Skeer Core  ││
│                                            │ (WAV reader,  ││
│                                            │  verification)││
│                                            └──────────────┘│
─────────────────────────────────────────────────────────────┘
```

## 与 traktor-clone 的区别

| 特性 | traktor-clone | autostem-flash-china |
|------|---------------|---------------------|
| **分析** | Stub 数据 (`bpm: 120, key: '8A'`) | 真实 WAV 分析 (duration, sampleRate, channels) |
| **Stem 路径** | 猜测 (`track-vocals.wav`) | 解析 `autostem.manifest.json` |
| **端口** | 3000 | 3001 (避免冲突) |
| **包名** | `xrblocks-example` | `autostem-flash-china` |
| **状态** | 原型/实验 | 生产就绪 |

## API 端点

| Endpoint | Method | 说明 |
|----------|--------|------|
| `/api/analyze` | POST | 分析音频文件，返回真实 duration/sampleRate/channels |
| `/api/queue/add` | POST | 添加文件到队列（不调用 CLI，直接写入 queue.json） |
| `/api/run` | POST | 运行队列（调用 `autostem run`） |
| `/api/verify` | POST | 验证结果文件夹（调用 `autostem verify`） |
| `/api/manifest` | POST | 获取结果文件夹的 manifest 文件（解析 stem 路径） |
| `/api/bench` | POST | 基准测试比较 |
| `/api/gemini/chat` | POST | Gemini AI 聊天 |

## 快速开始

```bash
cd apps/autostem-flash-china

# 安装依赖
npm install

# 启动开发服务器（端口 3001）
npm run dev

# 构建生产版本
npm run build

# 启动生产服务器
npm start
```

## 后端要求

- **Skeer autoSTEM CLI**: `artifacts/autostem-cli/autostem.exe`
- **Ableton Live 12.1+** (可选，仅当 `simulate=false` 时)

## 开发

### 添加新的 API 端点

1. 在 `server.ts` 中添加 Express 路由
2. 在 `src/services/backendBridge.ts` 中添加前端客户端方法
3. 在 `src/types.ts` 中添加类型定义（如果需要）

### 前端集成示例

```typescript
import { BackendBridge } from './services/backendBridge';

const backend = new BackendBridge('http://localhost:3001');

// 分析轨道
const analysis = await backend.analyzeTrack('/path/to/track.wav');
console.log(analysis.duration); // 真实时长（秒）
console.log(analysis.sampleRate); // 真实采样率

// 添加到队列
await backend.addFilesToQueue(['/path/to/track.wav'], '/path/to/output', 'all', 'speed');

// 运行队列
const result = await backend.runQueue('/path/to/output');

// 获取 manifest（解析 stem 路径）
const manifest = await backend.getManifest('/path/to/output/Track');
const stemPaths = manifest.files.map(f => f.path);
```

## 文件结构

```
apps/autostem-flash-china/
├── server.ts              # Express 服务器（包装 autostem CLI）
├── src/
│   ├── services/
│   │   ├── backendBeatAnalyzer.ts   # 后端分析器（真实数据）
│   │   ├── backendBridge.ts         # HTTP 客户端
│   │   ├── stemService.ts           # Stem 分离服务（解析 manifest）
│   │   └── audioEngine.ts           # 音频引擎（已更新）
│   ├── components/
│   │   ├── Console2D.ts             # 2D 控制台（Lit Web Component）
│   │   ├── SpatialXRBooth.ts        # 3D 空间 booth（Three.js）
│   │   └── ...
│   └── types.ts                     # 类型定义
├── public/
│   └── assets/
├── package.json
├── tsconfig.json
└── vite.config.ts
```

## 许可证

MIT

## 作者

Skeer

---

**注意**: 这是一个独立项目，与 `apps/traktor-clone/` 并行开发。两个项目可以独立运行，共享相同的后端 CLI。
