//go:build windows

package main

import (
	"fmt"
	"path/filepath"
	"runtime"
	"strings"
	"syscall"
	"unicode/utf16"
	"unsafe"
)

const (
	appTitle       = "txAdmin — Unban administrativo"
	wmDestroy      = 0x0002
	wmCommand      = 0x0111
	wmSetFont      = 0x0030
	buttonClicked  = 0
	buttonGetCheck = 0x00F0
	buttonChecked  = 1
	idBrowse       = 101
	idRevoke       = 102
)

var (
	kernel32  = syscall.NewLazyDLL("kernel32.dll") // A Windows KnownDLL.
	systemDir = windowsSystemDirectory()
	user32    = syscall.NewLazyDLL(filepath.Join(systemDir, "user32.dll"))
	comdlg32  = syscall.NewLazyDLL(filepath.Join(systemDir, "comdlg32.dll"))
	gdi32     = syscall.NewLazyDLL(filepath.Join(systemDir, "gdi32.dll"))

	createWindow      = user32.NewProc("CreateWindowExW")
	defaultWindowProc = user32.NewProc("DefWindowProcW")
	sendMessage       = user32.NewProc("SendMessageW")
	messageBox        = user32.NewProc("MessageBoxW")
	destroyWindow     = user32.NewProc("DestroyWindow")
	getOpenFileName   = comdlg32.NewProc("GetOpenFileNameW")

	mainWindow   uintptr
	pathEdit     uintptr
	banEdit      uintptr
	stoppedCheck uintptr
	revokeButton uintptr
	guiFont      uintptr
)

func windowsSystemDirectory() string {
	buffer := make([]uint16, 32768)
	length, _, err := kernel32.NewProc("GetSystemDirectoryW").Call(uintptr(unsafe.Pointer(&buffer[0])), uintptr(len(buffer)))
	if length == 0 || length >= uintptr(len(buffer)) {
		panic(fmt.Sprintf("não foi possível localizar o diretório do Windows: %v", err))
	}
	return syscall.UTF16ToString(buffer[:length])
}

func wide(value string) *uint16 {
	// Ban reasons and targets come from a file and can contain NUL characters.
	return syscall.StringToUTF16Ptr(strings.ReplaceAll(value, "\x00", "�"))
}

func showMessage(owner uintptr, message string, flags uintptr) uintptr {
	result, _, _ := messageBox.Call(owner, uintptr(unsafe.Pointer(wide(message))), uintptr(unsafe.Pointer(wide(appTitle))), flags)
	return result
}

// The pointer-sized members ensure the native Windows x64 structure layouts.
type windowClass struct {
	Size       uint32
	Style      uint32
	WindowProc uintptr
	ClassExtra int32
	WndExtra   int32
	Instance   uintptr
	Icon       uintptr
	Cursor     uintptr
	Background uintptr
	MenuName   *uint16
	ClassName  *uint16
	SmallIcon  uintptr
}

type point struct{ X, Y int32 }

type windowMessage struct {
	Window   uintptr
	Message  uint32
	WParam   uintptr
	LParam   uintptr
	Time     uint32
	Position point
	Private  uint32
}

type openFileName struct {
	Size            uint32
	Owner           uintptr
	Instance        uintptr
	Filter          *uint16
	CustomFilter    *uint16
	MaxCustomFilter uint32
	FilterIndex     uint32
	File            *uint16
	MaxFile         uint32
	FileTitle       *uint16
	MaxFileTitle    uint32
	InitialDir      *uint16
	Title           *uint16
	Flags           uint32
	FileOffset      uint16
	FileExtension   uint16
	DefaultExt      *uint16
	CustomData      uintptr
	Hook            uintptr
	TemplateName    *uint16
	Reserved        unsafe.Pointer
	ReservedDWORD   uint32
	FlagsEx         uint32
}

type processEntry struct {
	Size              uint32
	Usage             uint32
	ProcessID         uint32
	DefaultHeapID     uintptr
	ModuleID          uint32
	Threads           uint32
	ParentProcessID   uint32
	PriorityClassBase int32
	Flags             uint32
	Executable        [260]uint16
}

func localServerRunning() (bool, error) {
	snapshot, _, err := kernel32.NewProc("CreateToolhelp32Snapshot").Call(0x00000002, 0)
	if snapshot == ^uintptr(0) {
		return false, fmt.Errorf("não foi possível verificar se o FXServer está encerrado: %v", err)
	}
	defer kernel32.NewProc("CloseHandle").Call(snapshot)
	entry := processEntry{Size: uint32(unsafe.Sizeof(processEntry{}))}
	first := kernel32.NewProc("Process32FirstW")
	next := kernel32.NewProc("Process32NextW")
	ok, _, err := first.Call(snapshot, uintptr(unsafe.Pointer(&entry)))
	for ok != 0 {
		if strings.EqualFold(syscall.UTF16ToString(entry.Executable[:]), "FXServer.exe") {
			return true, nil
		}
		ok, _, err = next.Call(snapshot, uintptr(unsafe.Pointer(&entry)))
	}
	if err != syscall.Errno(18) { // ERROR_NO_MORE_FILES
		return false, fmt.Errorf("não foi possível concluir a verificação do FXServer: %v", err)
	}
	return false, nil
}

func requireStoppedServer() error {
	if checked, _, _ := sendMessage.Call(stoppedCheck, buttonGetCheck, 0, 0); checked != buttonChecked {
		return fmt.Errorf("encerre o txAdmin/FXServer e marque a confirmação antes de continuar; isso também vale para servidores em outro computador")
	}
	running, err := localServerRunning()
	if err != nil {
		return err
	}
	if running {
		return fmt.Errorf("o FXServer.exe está aberto neste computador; encerre o servidor e o txAdmin antes de alterar a base de bans")
	}
	return nil
}

func chooseDatabase() (string, error) {
	buffer := make([]uint16, 32768)
	copy(buffer, syscall.StringToUTF16("playersDB.json"))
	filter := utf16.Encode([]rune("Base do txAdmin (playersDB.json)\x00playersDB.json\x00Arquivos JSON (*.json)\x00*.json\x00\x00"))
	options := openFileName{
		Owner:       mainWindow,
		Filter:      &filter[0],
		FilterIndex: 1,
		File:        &buffer[0],
		MaxFile:     uint32(len(buffer)),
		Title:       wide("Selecione playersDB.json do seu servidor txAdmin"),
		DefaultExt:  wide("json"),
		// Explorer, existing path/file, no directory change, hide read-only, no history.
		Flags: 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008 | 0x00000004 | 0x02000000,
	}
	options.Size = uint32(unsafe.Sizeof(options))
	ok, _, _ := getOpenFileName.Call(uintptr(unsafe.Pointer(&options)))
	if ok == 0 {
		code, _, _ := comdlg32.NewProc("CommDlgExtendedError").Call()
		if code != 0 {
			return "", fmt.Errorf("falha ao selecionar a base de bans (código %d)", code)
		}
		return "", nil
	}
	return syscall.UTF16ToString(buffer), nil
}

func textOf(window uintptr) string {
	length, _, _ := user32.NewProc("GetWindowTextLengthW").Call(window)
	buffer := make([]uint16, length+1)
	user32.NewProc("GetWindowTextW").Call(window, uintptr(unsafe.Pointer(&buffer[0])), uintptr(len(buffer)))
	return strings.TrimSpace(syscall.UTF16ToString(buffer))
}

func short(value string, limit int) string {
	runes := []rune(value)
	if len(runes) > limit {
		return string(runes[:limit]) + "…"
	}
	return value
}

func submit() error {
	path, banID := textOf(pathEdit), textOf(banEdit)
	if path == "" || banID == "" {
		return fmt.Errorf("selecione playersDB.json e informe o ID exato do ban exibido pelo txAdmin")
	}
	if err := requireStoppedServer(); err != nil {
		return err
	}
	info, err := inspectBan(path, banID)
	if err != nil {
		return err
	}
	confirmation := fmt.Sprintf("Revogar este ban do seu servidor txAdmin?\n\nID: %s\nAlvo: %s\nMotivo: %s\n\nBase: %s\n\nSerá criado um backup ao lado de playersDB.json antes da alteração. Mantenha o txAdmin/FXServer encerrado até concluir.", short(info.ID, 120), short(info.Target, 300), short(info.Reason, 800), path)
	// MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2: cancellation is the default.
	if showMessage(mainWindow, confirmation, 0x00000004|0x00000020|0x00000100) != 6 {
		return nil
	}
	if err := requireStoppedServer(); err != nil {
		return err
	}
	backup, err := revokeBan(path, banID, info.DatabaseSHA256)
	if err != nil {
		return err
	}
	showMessage(mainWindow, "Ban revogado na base do txAdmin.\n\nBackup salvo em:\n"+backup+"\n\nVocê já pode iniciar o txAdmin/FXServer.", 0x00000040)
	return nil
}

func windowProc(window uintptr, message uint32, wParam, lParam uintptr) uintptr {
	switch message {
	case wmCommand:
		if uint16(wParam>>16) != buttonClicked {
			break
		}
		switch uint16(wParam) {
		case idBrowse:
			path, err := chooseDatabase()
			if err != nil {
				showMessage(window, err.Error(), 0x00000010)
			} else if path != "" {
				user32.NewProc("SetWindowTextW").Call(pathEdit, uintptr(unsafe.Pointer(wide(path))))
			}
			return 0
		case idRevoke:
			user32.NewProc("EnableWindow").Call(revokeButton, 0)
			err := submit()
			user32.NewProc("EnableWindow").Call(revokeButton, 1)
			if err != nil {
				showMessage(window, err.Error(), 0x00000010)
			}
			return 0
		}
	case wmDestroy:
		user32.NewProc("PostQuitMessage").Call(0)
		return 0
	}
	result, _, _ := defaultWindowProc.Call(window, uintptr(message), wParam, lParam)
	return result
}

func control(class, label string, style uint32, x, y, width, height int, id uintptr) (uintptr, error) {
	window, _, err := createWindow.Call(0, uintptr(unsafe.Pointer(wide(class))), uintptr(unsafe.Pointer(wide(label))), uintptr(style|0x50000000), uintptr(x), uintptr(y), uintptr(width), uintptr(height), mainWindow, id, 0, 0)
	if window == 0 {
		return 0, fmt.Errorf("não foi possível criar um controle da janela: %v", err)
	}
	sendMessage.Call(window, wmSetFont, guiFont, 1)
	return window, nil
}

func run() error {
	instance, _, err := kernel32.NewProc("GetModuleHandleW").Call(0)
	if instance == 0 {
		return fmt.Errorf("não foi possível iniciar a janela: %v", err)
	}
	cursor, _, _ := user32.NewProc("LoadCursorW").Call(0, 32512)
	className := wide("TxAdminLocalUnbanWindow")
	class := windowClass{
		Size:       uint32(unsafe.Sizeof(windowClass{})),
		WindowProc: syscall.NewCallback(windowProc),
		Instance:   instance,
		Cursor:     cursor,
		Background: 16, // COLOR_BTNFACE + 1, a system-owned brush.
		ClassName:  className,
	}
	if atom, _, err := user32.NewProc("RegisterClassExW").Call(uintptr(unsafe.Pointer(&class))); atom == 0 {
		return fmt.Errorf("não foi possível registrar a janela: %v", err)
	}
	// Caption, system menu and minimize button; fixed size prevents clipped controls.
	mainWindow, _, err = createWindow.Call(0, uintptr(unsafe.Pointer(className)), uintptr(unsafe.Pointer(wide(appTitle))), 0x00CA0000, 0x80000000, 0x80000000, 710, 355, 0, 0, instance, 0)
	if mainWindow == 0 {
		return fmt.Errorf("não foi possível criar a janela: %v", err)
	}
	defer destroyWindow.Call(mainWindow)
	guiFont, _, _ = gdi32.NewProc("GetStockObject").Call(17) // DEFAULT_GUI_FONT

	controls := []struct {
		class, label string
		style        uint32
		x, y, w, h   int
		id           uintptr
		destination  *uintptr
	}{
		{"STATIC", "Remova um ban do seu servidor txAdmin com backup automático.", 0, 20, 18, 660, 24, 0, nil},
		{"STATIC", "Base de bans (playersDB.json):", 0, 20, 56, 660, 20, 0, nil},
		{"EDIT", "", 0x00810080, 20, 80, 548, 26, 0, &pathEdit},
		{"BUTTON", "Procurar…", 0x00010000, 580, 80, 96, 26, idBrowse, nil},
		{"STATIC", "Caminho comum: txData\\<perfil>\\data\\playersDB.json", 0, 20, 111, 660, 20, 0, nil},
		{"STATIC", "ID do ban no txAdmin:", 0, 20, 145, 190, 22, 0, nil},
		{"EDIT", "", 0x00810080, 210, 141, 466, 26, 0, &banEdit},
		{"BUTTON", "O txAdmin/FXServer está encerrado", 0x00010003, 20, 185, 500, 26, 0, &stoppedCheck},
		{"BUTTON", "Desbanir", 0x00010000, 556, 226, 120, 32, idRevoke, &revokeButton},
		{"STATIC", "Somente bans do txAdmin. Outros anticheats e bans globais não são suportados.", 0, 20, 278, 660, 28, 0, nil},
	}
	for _, c := range controls {
		handle, err := control(c.class, c.label, c.style, c.x, c.y, c.w, c.h, c.id)
		if err != nil {
			return err
		}
		if c.destination != nil {
			*c.destination = handle
		}
	}
	user32.NewProc("ShowWindow").Call(mainWindow, 1)
	user32.NewProc("UpdateWindow").Call(mainWindow)
	user32.NewProc("SetFocus").Call(pathEdit)
	var message windowMessage
	for {
		result, _, err := user32.NewProc("GetMessageW").Call(uintptr(unsafe.Pointer(&message)), 0, 0, 0)
		if int32(result) == -1 {
			return fmt.Errorf("falha na interface do Windows: %v", err)
		}
		if result == 0 {
			return nil
		}
		if handled, _, _ := user32.NewProc("IsDialogMessageW").Call(mainWindow, uintptr(unsafe.Pointer(&message))); handled != 0 {
			continue
		}
		user32.NewProc("TranslateMessage").Call(uintptr(unsafe.Pointer(&message)))
		user32.NewProc("DispatchMessageW").Call(uintptr(unsafe.Pointer(&message)))
	}
}

func main() {
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()
	if err := run(); err != nil {
		showMessage(0, err.Error(), 0x00000010)
	}
}
