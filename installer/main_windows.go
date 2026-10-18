//go:build windows

package main

import (
	"embed"
	"fmt"
	"io/fs"
	"path/filepath"
	"runtime"
	"strings"
	"syscall"
	"unicode/utf16"
	"unsafe"
)

//go:embed payload/fxmanifest.lua payload/server.lua payload/lib/simulator.lua payload/README.md
var embeddedFiles embed.FS

var (
	systemDirectory = windowsSystemDirectory()
	user32          = syscall.NewLazyDLL(filepath.Join(systemDirectory, "user32.dll"))
	shell32         = syscall.NewLazyDLL(filepath.Join(systemDirectory, "shell32.dll"))
	ole32           = syscall.NewLazyDLL(filepath.Join(systemDirectory, "ole32.dll"))
	comdlg32        = syscall.NewLazyDLL(filepath.Join(systemDirectory, "comdlg32.dll"))
	messageBox      = user32.NewProc("MessageBoxW")
	getOpenFileName = comdlg32.NewProc("GetOpenFileNameW")
	dialogError     = comdlg32.NewProc("CommDlgExtendedError")
	browseForFolder = shell32.NewProc("SHBrowseForFolderW")
	getFolderPath   = shell32.NewProc("SHGetPathFromIDListEx")
)

func windowsSystemDirectory() string {
	// kernel32 is a Windows KnownDLL. Load every other DLL by its system path.
	buffer := make([]uint16, 32768)
	getSystemDirectory := syscall.NewLazyDLL("kernel32.dll").NewProc("GetSystemDirectoryW")
	length, _, err := getSystemDirectory.Call(uintptr(unsafe.Pointer(&buffer[0])), uintptr(len(buffer)))
	if length == 0 || length >= uintptr(len(buffer)) {
		panic(fmt.Sprintf("não foi possível localizar os arquivos de sistema do Windows: %v", err))
	}
	return syscall.UTF16ToString(buffer[:length])
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

type browseInfo struct {
	Owner       uintptr
	Root        uintptr
	DisplayName *uint16
	Title       *uint16
	Flags       uint32
	Callback    uintptr
	Param       uintptr
	Image       int32
}

func wide(value string) *uint16 {
	return syscall.StringToUTF16Ptr(value)
}

func message(text string, flags uintptr) uintptr {
	result, _, _ := messageBox.Call(0, uintptr(unsafe.Pointer(wide(text))), uintptr(unsafe.Pointer(wide("HWID Lab — Instalador do simulador"))), flags)
	return result
}

func chooseConfig() (string, error) {
	buffer := make([]uint16, 32768)
	copy(buffer, syscall.StringToUTF16("server.cfg"))
	filter := utf16.Encode([]rune("Configuração do servidor (*.cfg)\x00*.cfg\x00Todos os arquivos\x00*.*\x00\x00"))
	options := openFileName{
		Filter:      &filter[0],
		File:        &buffer[0],
		MaxFile:     uint32(len(buffer)),
		Title:       wide("Selecione o server.cfg do servidor FiveM"),
		Flags:       0x00080000 | 0x00001000 | 0x00000800 | 0x00000008 | 0x00000004,
		DefaultExt:  wide("cfg"),
		FilterIndex: 1,
	}
	options.Size = uint32(unsafe.Sizeof(options))
	ok, _, _ := getOpenFileName.Call(uintptr(unsafe.Pointer(&options)))
	if ok == 0 {
		code, _, _ := dialogError.Call()
		if code != 0 {
			return "", fmt.Errorf("não foi possível abrir a seleção de arquivo (código %d)", code)
		}
		return "", nil
	}
	return syscall.UTF16ToString(buffer), nil
}

func chooseResources() (string, error) {
	displayName := make([]uint16, 32768)
	options := browseInfo{
		DisplayName: &displayName[0],
		Title:       wide("Selecione a pasta resources do MESMO servidor FiveM"),
		Flags:       0x00000001 | 0x00000040 | 0x00000010,
	}
	item, _, _ := browseForFolder.Call(uintptr(unsafe.Pointer(&options)))
	if item == 0 {
		return "", nil
	}
	defer ole32.NewProc("CoTaskMemFree").Call(item)
	buffer := make([]uint16, 32768)
	ok, _, _ := getFolderPath.Call(item, uintptr(unsafe.Pointer(&buffer[0])), uintptr(len(buffer)), 0)
	if ok == 0 {
		return "", fmt.Errorf("não foi possível ler o caminho da pasta resources")
	}
	path := syscall.UTF16ToString(buffer)
	if !strings.EqualFold(filepath.Base(filepath.Clean(path)), "resources") {
		return "", fmt.Errorf("selecione a pasta chamada resources, dentro dos dados do servidor")
	}
	return path, nil
}

func run() error {
	config, err := chooseConfig()
	if err != nil || config == "" {
		return err
	}
	resources, err := chooseResources()
	if err != nil || resources == "" {
		return err
	}
	target := filepath.Join(resources, "[local]", "hwid_lab")
	confirmation := fmt.Sprintf("Instalar o SIMULADOR HWID Lab?\n\nDestino: %s\n\nConfiguração: %s\n\nSerá adicionada a linha ensure hwid_lab se necessário, com backup da configuração original.\n\nO recurso usa apenas identificadores fictícios para testes.", target, config)
	// MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2; cancellation is the default.
	if message(confirmation, 0x00000004|0x00000020|0x00000100) != 6 {
		return nil
	}
	payload := make(map[string][]byte)
	for _, name := range []string{"fxmanifest.lua", "server.lua", "lib/simulator.lua", "README.md"} {
		contents, err := fs.ReadFile(embeddedFiles, "payload/"+name)
		if err != nil {
			return fmt.Errorf("arquivo interno indisponível: %s: %w", name, err)
		}
		payload[name] = contents
	}
	if err := install(config, resources, payload); err != nil {
		return err
	}
	message("Simulador instalado.\n\nCom o servidor aberto, execute no console:\nrefresh\nensure hwid_lab\nhwidlab help\n\nSe o servidor estiver fechado, o recurso iniciará com ele. Os comandos de teste estão no README dentro da pasta hwid_lab.", 0x00000040)
	return nil
}

func main() {
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()
	status, _, _ := ole32.NewProc("CoInitializeEx").Call(0, 2)
	if int32(status) < 0 {
		message(fmt.Sprintf("Não foi possível iniciar os diálogos do Windows (código 0x%x).", status), 0x00000010)
		return
	}
	defer ole32.NewProc("CoUninitialize").Call()
	if err := run(); err != nil {
		message(err.Error(), 0x00000010)
	}
}
