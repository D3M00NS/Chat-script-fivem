package main

import (
	"bytes"
	"encoding/binary"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"time"
	"unicode/utf16"
	"unicode/utf8"
)

var payloadFiles = []string{"fxmanifest.lua", "server.lua", "lib/simulator.lua", "README.md"}
var activeResource = regexp.MustCompile(`(?i)^[ \t]*(ensure|start)[ \t]+("hwid_lab"|'hwid_lab'|hwid_lab)[ \t]*(#.*)?$`)

// install installs only the fictional laboratory resource into a selected server.
func install(configPath, resourcesDir string, payload map[string][]byte) (err error) {
	if len(payload) != len(payloadFiles) {
		return fmt.Errorf("o pacote do simulador tem arquivos inesperados ou ausentes")
	}
	for _, name := range payloadFiles {
		if _, ok := payload[name]; !ok {
			return fmt.Errorf("o pacote do simulador não contém %s", name)
		}
	}
	configPath, err = filepath.Abs(configPath)
	if err != nil {
		return err
	}
	resourcesDir, err = filepath.Abs(resourcesDir)
	if err != nil {
		return err
	}
	for _, path := range []string{configPath, resourcesDir} {
		if err = noSymlinks(path); err != nil {
			return err
		}
	}
	info, err := os.Stat(configPath)
	if err != nil || !info.Mode().IsRegular() {
		return fmt.Errorf("selecione um server.cfg existente e regular")
	}
	resourceInfo, err := os.Stat(resourcesDir)
	if err != nil || !resourceInfo.IsDir() {
		return fmt.Errorf("selecione uma pasta resources existente")
	}
	original, err := os.ReadFile(configPath)
	if err != nil {
		return fmt.Errorf("não foi possível ler server.cfg: %w", err)
	}
	updated, err := enableResource(original)
	if err != nil {
		return err
	}
	localDir := filepath.Join(resourcesDir, "[local]")
	target := filepath.Join(localDir, "hwid_lab")
	if err = noSymlinks(target); err != nil {
		return err
	}
	existing, err := matchingResource(target, payload)
	if err != nil {
		return err
	}
	createdLocal, installed := false, false
	defer func() {
		if err != nil {
			if installed {
				_ = os.RemoveAll(target)
			}
			if createdLocal {
				_ = os.Remove(localDir)
			}
		}
	}()
	if !existing {
		if err = os.Mkdir(localDir, 0755); err == nil {
			createdLocal = true
		} else if !os.IsExist(err) {
			return fmt.Errorf("não foi possível criar [local]: %w", err)
		}
		if err = noSymlinks(localDir); err != nil {
			return err
		}
		stage, stageErr := os.MkdirTemp(localDir, ".hwidlab-stage-")
		if stageErr != nil {
			return fmt.Errorf("não foi possível preparar o simulador: %w", stageErr)
		}
		defer os.RemoveAll(stage)
		if err = os.Chmod(stage, 0755); err != nil {
			return err
		}
		if err = os.Mkdir(filepath.Join(stage, "lib"), 0755); err != nil {
			return err
		}
		for _, name := range payloadFiles {
			if err = os.WriteFile(filepath.Join(stage, filepath.FromSlash(name)), payload[name], 0644); err != nil {
				return fmt.Errorf("não foi possível preparar %s: %w", name, err)
			}
		}
		if _, statErr := os.Lstat(target); !os.IsNotExist(statErr) {
			return fmt.Errorf("a pasta hwid_lab apareceu durante a instalação; tente novamente")
		}
		if err = os.Rename(stage, target); err != nil {
			return fmt.Errorf("não foi possível instalar o simulador: %w", err)
		}
		installed = true
	}
	if bytes.Equal(original, updated) {
		return nil
	}
	// Prepare the replacement before taking a backup or changing the configuration.
	replacement, err := temporaryFile(filepath.Dir(configPath), ".hwidlab-config-*", updated, info.Mode().Perm())
	if err != nil {
		return fmt.Errorf("não foi possível preparar server.cfg: %w", err)
	}
	defer os.Remove(replacement)
	if err = noSymlinks(configPath); err != nil {
		return err
	}
	current, err := os.ReadFile(configPath)
	if err != nil || !bytes.Equal(current, original) {
		return fmt.Errorf("server.cfg mudou durante a instalação; tente novamente")
	}
	backupPattern := filepath.Base(configPath) + ".hwidlab-backup-" + time.Now().UTC().Format("20060102T150405.000000000Z") + "-*"
	if _, err = temporaryFile(filepath.Dir(configPath), backupPattern, original, info.Mode().Perm()); err != nil {
		return fmt.Errorf("não foi possível salvar o backup de server.cfg: %w", err)
	}
	// Rename within the same directory uses the platform's replacement operation.
	if err = os.Rename(replacement, configPath); err != nil {
		return fmt.Errorf("não foi possível atualizar server.cfg; o original e o backup foram preservados: %w", err)
	}
	return nil
}

// Existing ancestors and the selected object must never redirect through a link.
func noSymlinks(path string) error {
	for current := filepath.Clean(path); ; current = filepath.Dir(current) {
		info, err := os.Lstat(current)
		if err != nil && !os.IsNotExist(err) {
			return fmt.Errorf("não foi possível verificar %s: %w", current, err)
		}
		// Go 1.23+ reports Windows junctions and other name-surrogate
		// reparse points as ModeIrregular rather than ModeSymlink.
		if err == nil && info.Mode()&(os.ModeSymlink|os.ModeIrregular) != 0 {
			return fmt.Errorf("links simbólicos ou redirecionamentos não são permitidos: %s", current)
		}
		if parent := filepath.Dir(current); parent == current {
			break
		}
	}
	return nil
}

func matchingResource(target string, payload map[string][]byte) (bool, error) {
	info, err := os.Lstat(target)
	if os.IsNotExist(err) {
		return false, nil
	}
	if err != nil {
		return false, err
	}
	if !info.IsDir() {
		return false, fmt.Errorf("hwid_lab já existe e não é uma pasta")
	}
	seen := map[string]bool{}
	err = filepath.WalkDir(target, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		name, relErr := filepath.Rel(target, path)
		if relErr != nil {
			return relErr
		}
		name = filepath.ToSlash(name)
		if entry.Type()&os.ModeSymlink != 0 {
			return fmt.Errorf("hwid_lab contém um link simbólico: %s", name)
		}
		if entry.IsDir() {
			if name == "." || name == "lib" {
				return nil
			}
			return fmt.Errorf("hwid_lab contém uma pasta adicional: %s", name)
		}
		expected, allowed := payload[name]
		if !allowed || !entry.Type().IsRegular() {
			return fmt.Errorf("hwid_lab contém um arquivo inesperado: %s", name)
		}
		actual, readErr := os.ReadFile(path)
		if readErr != nil {
			return readErr
		}
		if !bytes.Equal(actual, expected) {
			return fmt.Errorf("hwid_lab já contém alterações em %s; nenhum arquivo será sobrescrito", name)
		}
		seen[name] = true
		return nil
	})
	if err != nil {
		return false, err
	}
	if len(seen) != len(payload) {
		return false, fmt.Errorf("hwid_lab já existe e está incompleto; nenhum arquivo será sobrescrito")
	}
	return true, nil
}

func temporaryFile(dir, pattern string, content []byte, mode fs.FileMode) (path string, err error) {
	file, err := os.CreateTemp(dir, pattern)
	if err != nil {
		return "", err
	}
	path = file.Name()
	defer func() {
		_ = file.Close()
		if err != nil {
			_ = os.Remove(path)
		}
	}()
	if err = file.Chmod(mode); err != nil {
		return path, err
	}
	if _, err = file.Write(content); err != nil {
		return path, err
	}
	if err = file.Sync(); err != nil {
		return path, err
	}
	err = file.Close()
	return path, err
}

func enableResource(original []byte) ([]byte, error) {
	text, encoding, err := decodeConfig(original)
	if err != nil {
		return nil, err
	}
	for _, line := range strings.FieldsFunc(text, func(r rune) bool { return r == '\r' || r == '\n' }) {
		if activeResource.MatchString(line) {
			return original, nil
		}
	}
	newline := "\n"
	if index := strings.IndexAny(text, "\r\n"); index >= 0 && text[index] == '\r' {
		newline = "\r"
		if index+1 < len(text) && text[index+1] == '\n' {
			newline = "\r\n"
		}
	}
	if len(text) > 0 && !strings.HasSuffix(text, "\n") && !strings.HasSuffix(text, "\r") {
		text += newline
	}
	text += "ensure hwid_lab" + newline
	if encoding == "utf8" {
		return []byte(text), nil
	}
	if encoding == "utf8bom" {
		return append([]byte{0xef, 0xbb, 0xbf}, []byte(text)...), nil
	}
	var order binary.ByteOrder = binary.LittleEndian
	bom := []byte{0xff, 0xfe}
	if encoding == "utf16be" {
		order, bom = binary.BigEndian, []byte{0xfe, 0xff}
	}
	result := append([]byte(nil), bom...)
	for _, unit := range utf16.Encode([]rune(text)) {
		var data [2]byte
		order.PutUint16(data[:], unit)
		result = append(result, data[:]...)
	}
	return result, nil
}

func decodeConfig(data []byte) (string, string, error) {
	encoding := "utf8"
	if bytes.HasPrefix(data, []byte{0xef, 0xbb, 0xbf}) {
		data, encoding = data[3:], "utf8bom"
	}
	if bytes.HasPrefix(data, []byte{0xff, 0xfe}) || bytes.HasPrefix(data, []byte{0xfe, 0xff}) {
		var order binary.ByteOrder = binary.LittleEndian
		encoding = "utf16le"
		if data[0] == 0xfe {
			order, encoding = binary.BigEndian, "utf16be"
		}
		data = data[2:]
		if len(data)%2 != 0 {
			return "", "", fmt.Errorf("server.cfg contém UTF-16 incompleto")
		}
		units := make([]uint16, len(data)/2)
		for i := range units {
			units[i] = order.Uint16(data[i*2:])
		}
		for i := 0; i < len(units); i++ {
			if units[i] >= 0xd800 && units[i] <= 0xdbff {
				if i+1 >= len(units) || units[i+1] < 0xdc00 || units[i+1] > 0xdfff {
					return "", "", fmt.Errorf("server.cfg contém UTF-16 inválido")
				}
				i++
			} else if units[i] >= 0xdc00 && units[i] <= 0xdfff {
				return "", "", fmt.Errorf("server.cfg contém UTF-16 inválido")
			}
		}
		return string(utf16.Decode(units)), encoding, nil
	}
	if !utf8.Valid(data) || bytes.IndexByte(data, 0) >= 0 {
		return "", "", fmt.Errorf("server.cfg deve usar UTF-8 ou UTF-16 com BOM")
	}
	return string(data), encoding, nil
}
