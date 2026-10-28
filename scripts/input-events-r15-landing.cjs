// Same-site loopback navigation page; never proxies a request or bypasses the editor's security.
const http = require('node:http');
http.createServer((request, response) => {
  response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store',
    'Content-Security-Policy': "default-src 'none'; frame-ancestors 'none'; base-uri 'none'" });
  response.end('<!doctype html><html lang="zh-Hant"><meta charset="utf-8"><title>R15 本機驗證入口</title><h1>R15 本機驗證入口</h1><p>只供本機驗證導向，沒有專案或檔案 API。</p><a href="http://127.0.0.1:5450/?source=input-events-r15-chrome">開啟 R15 隔離工作檯</a></html>');
}).listen(5451, '127.0.0.1', () => console.log('R15 loopback navigation page on5451'));
