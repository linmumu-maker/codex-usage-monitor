# Third-party references

Reference project: https://github.com/D1NOOO/codex-usage-monitor
License verified: MIT.
Sources inspected: README.md, src/CodexRateMonitor.cs, scripts/build.ps1, LICENSE on 2026-10-08.

The reference uses .NET Framework / WinForms, a persistent codex app-server child,
a tray menu and a window-edge overlay. This project independently implements WPF,
input-area-above placement, conservative UI Automation collision checks and a compact bar.
Protocol handshake, native executable discovery and Framework compiler usage are informed by the reference.
No source files are copied wholesale. The reference's direct authentication-token read,
reset-credit HTTP client, updater and startup-registration features are not included.

Upstream license retained below for attribution:

MIT License

Copyright (c) 2026 Codex Rate Monitor contributors

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
