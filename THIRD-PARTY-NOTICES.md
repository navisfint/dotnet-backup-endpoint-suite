# 第三方开源组件与许可证清单

本工程在开发过程中使用了以下开源组件。按照各组件的许可证要求，此处保留其原始版权声明与许可证信息。

本工程**主体业务代码为自主开发**，开源组件仅作为基础依赖使用，未对组件源码进行修改。

---

## 一、组件清单

| # | 组件 | 版本 | 用途 | 许可证 | 版权归属 |
|---|---|---|---|---|---|
| 1 | .NET Runtime / ASP.NET Core | 8.0 | 应用运行时与 Web 框架 | MIT | Copyright (c) .NET Foundation and Contributors |
| 2 | Microsoft.Data.Sqlite | 10.0.13 | SQLite 数据库访问 | MIT | Copyright (c) .NET Foundation and Contributors |
| 3 | SQLitePCLRaw（bundle_e_sqlite3 / core / provider） | 随包引入 | SQLite 本地互操作层 | Apache-2.0 | Copyright (c) Eric Sink 及 SQLitePCLRaw 贡献者 |
| 4 | SQLite | 3.x（随包引入） | 嵌入式数据库引擎 | Public Domain | SQLite 作者声明为公有领域 |
| 5 | ZstdSharp.Port | 0.8.8 | 备份数据 Zstandard 压缩 | MIT | Copyright (c) 2021 ZstdSharp 贡献者 |
| 6 | Zstandard (zstd) | 随包引入 | Zstandard 压缩算法实现 | BSD-3-Clause | Copyright (c) Meta Platforms, Inc. and affiliates |
| 7 | Microsoft.Extensions.Hosting | 8.0.0 | 终端代理主机构建 | MIT | Copyright (c) .NET Foundation and Contributors |
| 8 | Microsoft.Extensions.Hosting.WindowsServices | 8.0.0 | 终端代理注册为 Windows 服务 | MIT | Copyright (c) .NET Foundation and Contributors |
| 9 | Microsoft.Extensions.Http | 8.0.0 | 终端代理 HTTP 通信 | MIT | Copyright (c) .NET Foundation and Contributors |

---

## 二、许可证全文

### MIT License

```
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Apache License 2.0（摘要）

```
Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```

### BSD 3-Clause License

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice,
   this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors
   may be used to endorse or promote products derived from this software
   without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES ARE DISCLAIMED.
```

---

## 三、商标声明

本工程产品名称与界面标识均为自研命名，**未使用任何第三方产品的商标或注册商标**。

---

Copyright © 2026 示例科技有限公司. All rights reserved.
