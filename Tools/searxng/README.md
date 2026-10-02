# 团团本机网页搜索

默认连接地址：`http://127.0.0.1:8888`。SearXNG 只映射到本机回环地址；配置已打开 JSON 搜索格式。

## 启动和停止

在轻量包目录打开 PowerShell，运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\start-searxng.ps1
```

停止服务：

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\stop-searxng.ps1
```

脚本使用官方 `searxng/searxng` 镜像，不会安装 Docker Desktop。设置文件和 SearXNG 缓存放在此资料夹下；Docker Desktop 的镜像文件位置仍由 Docker 自己的磁盘设置决定。C 盘空间不足时，可在 Docker Desktop 下载镜像前检查其磁盘映像位置，或直接在团团网页搜索设置中选择 Chrome / Google 或 Bing。

容器启动后，在团团聊天窗“网页搜索”旁点“设置”，选择“本地 SearXNG”，保留默认地址并点“测试当前方式”。也可以填入现有的 SearXNG 服务地址。

搜索服务只接收本轮搜索词，不会收到聊天历史或本地文件。SearXNG 会按自身配置向它使用的搜索引擎发起查询。

参考：[SearXNG Docker 安装文档](https://docs.searxng.org/admin/installation-docker.html)、[SearXNG 搜索 API](https://docs.searxng.org/dev/search_api.html)。
