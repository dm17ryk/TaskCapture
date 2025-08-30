# 📸 Interview Task Solver

**TaskSolver** is a desktop tool for capturing <img valign="bottom" alt="Coderpad Logo IDE" width="96" height="20" alt="coderpad_logo_ide" src="https://github.com/user-attachments/assets/f863b2bd-55b3-48ca-9cf7-0cca9d1e1297" />
programming tasks directly from your browser screen and getting AI-generated solutions. It streamlines grabbing a problem description and code context (e.g., from a web-based coding platform) and sending them to an AI model for analysis.  

⚡ **All with hotkeys – no need to leave your browser window!**  
🔬 Created **only for research and testing purposes**.

---

## ✨ Features

- 🖼 **Dual-Panel Screenshot Capture** – left & right halves of the target window, automatically cropped.  
- ⌨️ **Global Hotkeys** – operate the app without leaving your active window.  
- 🤖 **AI Model Integration** – send screenshots to OpenAI models for problem solving.  
- 🔽 **Model Selection** – choose between multiple GPT models (e.g., GPT-5, mini, nano).  
- 📂 **Task Management** – each session is stored in its own folder with captures and results.  
- 📝 **Log Panel** – in-app real-time logging of every action and error.  
- 🌐 **Result Viewer** – AI output is shown in a styled HTML window with syntax-highlighted code.

---

## ⚡ Hotkey Workflow

Stay focused in your browser – everything is done with shortcuts:

| Hotkey | Action |
|--------|--------|
| **Ctrl + Alt + H** | Select active window |
| **Ctrl + Alt + Z** | Start a new task |
| **Ctrl + Alt + G** | Capture screenshot (left & right halves) |
| **Ctrl + Alt + E** | Send all captures to AI and get solution |

Additional UI buttons:  
🔘 *Open captures folder* • 🔘 *Clear captures* • 🔽 *Model selector*

---

## 🛠 Usage Flow

1. Open the **target task** (e.g., <img valign="bottom" alt="Coderpad Logo IDE" width="96" height="20" alt="coderpad_logo_ide" src="https://github.com/user-attachments/assets/f863b2bd-55b3-48ca-9cf7-0cca9d1e1297" /> coding challenge in browser).
2. Select **AI model** from the dropdown. 
3. Press **Ctrl+Alt+H** → focuse browser, select the active window.  
4. Press **Ctrl+Alt+Z** → create a new task.  
5. Press **Ctrl+Alt+G** → capture screenshots (repeat after scrolling, capture->scrol->capture->scroll etc...).   
6. Press **Ctrl+Alt+E** → send to AI.  
7. 📜 Review the solution in the **Result Window** (and saved as `result.html`).  

All actions are logged in real-time in the log panel.

---

## ⚙️ Setup

- 🖥 **OS**: Windows 10/11  
- 💻 **Runtime**: [.NET 8.0](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)  
- 🌐 **WebView2**: Required for result viewer  
- 🔑 **API Key**: Set environment variable `OPENAI_API_KEY`  

```powershell
setx OPENAI_API_KEY "your_api_key_here"
```

Build from source with Visual Studio 2022 or `dotnet build`.  
Run `TaskCapture.exe` to start.

---

## 📜 Disclaimer

> ⚠️ **TaskSolver is intended for research and testing only.**  
> AI-generated solutions are not guaranteed to be correct.  
> Do **not** use it for cheating, exams, or violating any platform rules.  

---

## 📄 License

This project is licensed under the **GNU GPL v3.0**.  

- ✅ Free to use, modify, and share.  
- 🔄 Any derivative must also remain open-source under GPL-3.0.  
- 🛡 Protects against proprietary use without permission.  

See the [LICENSE](./LICENSE) file for details.

---

🚀 *Happy experimenting with TaskSolver! (Tested on coderpad.io)*  
