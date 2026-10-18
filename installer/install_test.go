package main

import (
	"bytes"
	"encoding/binary"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"unicode/utf16"
)

func testPayload() map[string][]byte {
	return map[string][]byte{
		"fxmanifest.lua":    []byte("fx_version 'cerulean'\n"),
		"server.lua":        []byte("-- laboratório fictício\n"),
		"lib/simulator.lua": []byte("return {}\n"),
		"README.md":         []byte("# HWID Lab\n"),
	}
}

func testServer(t *testing.T, config []byte) (string, string, string) {
	t.Helper()
	base := t.TempDir()
	resources := filepath.Join(base, "resources")
	if err := os.Mkdir(resources, 0755); err != nil {
		t.Fatal(err)
	}
	path := filepath.Join(base, "server.cfg")
	if err := os.WriteFile(path, config, 0640); err != nil {
		t.Fatal(err)
	}
	return path, resources, filepath.Join(resources, "[local]", "hwid_lab")
}

func read(t *testing.T, path string) []byte {
	t.Helper()
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	return data
}

func backups(t *testing.T, config string) []string {
	t.Helper()
	paths, err := filepath.Glob(config + ".hwidlab-backup-*")
	if err != nil {
		t.Fatal(err)
	}
	return paths
}

func TestInstallPreservesConfigAndIsIdempotent(t *testing.T) {
	original := []byte("# configuração do servidor\r\nensure chat")
	config, resources, target := testServer(t, original)
	payload := testPayload()
	if err := install(config, resources, payload); err != nil {
		t.Fatal(err)
	}
	want := append(append([]byte(nil), original...), []byte("\r\nensure hwid_lab\r\n")...)
	if got := read(t, config); !bytes.Equal(got, want) {
		t.Fatalf("config: %q, want %q", got, want)
	}
	for name, content := range payload {
		if got := read(t, filepath.Join(target, filepath.FromSlash(name))); !bytes.Equal(got, content) {
			t.Fatalf("payload changed: %s", name)
		}
	}
	saved := backups(t, config)
	if len(saved) != 1 || !bytes.Equal(read(t, saved[0]), original) {
		t.Fatal("missing exact original backup")
	}
	if err := install(config, resources, payload); err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(read(t, config), want) || len(backups(t, config)) != 1 {
		t.Fatal("second install modified configuration or backup")
	}
	entries, err := os.ReadDir(filepath.Dir(target))
	if err != nil || len(entries) != 1 {
		t.Fatalf("staging files left behind: %v, %v", entries, err)
	}
}

func TestActiveResourcePreservesEveryByte(t *testing.T) {
	for _, text := range []string{
		"ensure hwid_lab", "  EnSuRe\t\"HWID_LAB\"   # já ativo\r\n", "start 'hwid_lab'\n",
		"# header\n\tSTART\thwid_lab\t# comment\nensure chat\n",
	} {
		t.Run(text, func(t *testing.T) {
			config, resources, _ := testServer(t, []byte(text))
			if err := install(config, resources, testPayload()); err != nil {
				t.Fatal(err)
			}
			if !bytes.Equal(read(t, config), []byte(text)) {
				t.Fatal("active configuration bytes changed")
			}
			if len(backups(t, config)) != 0 {
				t.Fatal("unnecessary backup")
			}
		})
	}
}

func TestCommentsAndOtherResourcesDoNotCountAsActive(t *testing.T) {
	for _, text := range []string{"# ensure hwid_lab", "; ensure hwid_lab", "// ensure hwid_lab", "ensure hwid_lab_other", "ensure \"hwid_lab_other\""} {
		t.Run(text, func(t *testing.T) {
			got, err := enableResource([]byte(text + "\n"))
			if err != nil {
				t.Fatal(err)
			}
			if string(got) != text+"\nensure hwid_lab\n" {
				t.Fatalf("incorrect configuration: %q", got)
			}
		})
	}
}

func encodeUTF16(text string, big bool) []byte {
	var order binary.ByteOrder = binary.LittleEndian
	result := []byte{0xff, 0xfe}
	if big {
		order, result = binary.BigEndian, []byte{0xfe, 0xff}
	}
	for _, value := range utf16.Encode([]rune(text)) {
		var data [2]byte
		order.PutUint16(data[:], value)
		result = append(result, data[:]...)
	}
	return result
}

func TestConfigurationEncodings(t *testing.T) {
	text := "# ação 🎮\r\nensure chat\r\n"
	tests := map[string]func(string) []byte{
		"utf8":    func(s string) []byte { return []byte(s) },
		"utf8bom": func(s string) []byte { return append([]byte{0xef, 0xbb, 0xbf}, []byte(s)...) },
		"utf16le": func(s string) []byte { return encodeUTF16(s, false) },
		"utf16be": func(s string) []byte { return encodeUTF16(s, true) },
	}
	for name, encode := range tests {
		t.Run(name, func(t *testing.T) {
			original := encode(text)
			config, resources, _ := testServer(t, original)
			if err := install(config, resources, testPayload()); err != nil {
				t.Fatal(err)
			}
			want := encode(text + "ensure hwid_lab\r\n")
			if !bytes.Equal(read(t, config), want) {
				t.Fatal("encoding, newline, or original text changed")
			}
			if !bytes.Equal(read(t, backups(t, config)[0]), original) {
				t.Fatal("backup encoding changed")
			}
			if err := install(config, resources, testPayload()); err != nil {
				t.Fatal(err)
			}
			if !bytes.Equal(read(t, config), want) || len(backups(t, config)) != 1 {
				t.Fatal("encoded idempotent install changed bytes")
			}
		})
	}
}

func TestInvalidEncodingDoesNotInstall(t *testing.T) {
	for _, data := range [][]byte{{0xff}, {0xff, 0xfe, 0x00}, {0xff, 0xfe, 0x00, 0xd8}, {0xfe, 0xff, 0xdc, 0x00}, {'e', 0, 'n', 0}} {
		config, resources, target := testServer(t, data)
		if err := install(config, resources, testPayload()); err == nil {
			t.Fatal("accepted invalid encoding")
		}
		if !bytes.Equal(read(t, config), data) || len(backups(t, config)) != 0 {
			t.Fatal("changed invalid configuration")
		}
		if _, err := os.Lstat(target); !os.IsNotExist(err) {
			t.Fatal("installed resource with invalid configuration")
		}
	}
}

func TestExistingUserResourceIsNeverOverwritten(t *testing.T) {
	for _, change := range []string{"modified", "extra-file", "extra-dir", "missing"} {
		t.Run(change, func(t *testing.T) {
			original := []byte("ensure chat\n")
			config, resources, target := testServer(t, original)
			if err := os.MkdirAll(filepath.Join(target, "lib"), 0755); err != nil {
				t.Fatal(err)
			}
			payload := testPayload()
			for name, content := range payload {
				if err := os.WriteFile(filepath.Join(target, filepath.FromSlash(name)), content, 0644); err != nil {
					t.Fatal(err)
				}
			}
			var changed string
			switch change {
			case "modified":
				changed = filepath.Join(target, "server.lua")
				if err := os.WriteFile(changed, []byte("user changes"), 0644); err != nil {
					t.Fatal(err)
				}
			case "extra-file":
				changed = filepath.Join(target, "user.txt")
				if err := os.WriteFile(changed, []byte("user changes"), 0644); err != nil {
					t.Fatal(err)
				}
			case "extra-dir":
				changed = filepath.Join(target, "user-dir")
				if err := os.Mkdir(changed, 0755); err != nil {
					t.Fatal(err)
				}
			case "missing":
				if err := os.Remove(filepath.Join(target, "README.md")); err != nil {
					t.Fatal(err)
				}
			}
			if err := install(config, resources, payload); err == nil {
				t.Fatal("accepted altered resource")
			}
			if !bytes.Equal(read(t, config), original) || len(backups(t, config)) != 0 {
				t.Fatal("changed configuration after refusing resource")
			}
			if changed != "" {
				if _, err := os.Lstat(changed); err != nil {
					t.Fatal("removed user file", err)
				}
			}
		})
	}
}

func TestUnsafePayloadPathsAreRejected(t *testing.T) {
	for _, name := range []string{"../outside", "/outside", "C:\\outside", "lib/../../outside", "lib\\..\\outside", "server.lua/extra"} {
		t.Run(name, func(t *testing.T) {
			config, resources, target := testServer(t, []byte(""))
			payload := testPayload()
			delete(payload, "README.md")
			payload[name] = []byte("untrusted")
			if err := install(config, resources, payload); err == nil {
				t.Fatal("accepted unsafe payload")
			}
			if len(read(t, config)) != 0 || len(backups(t, config)) != 0 {
				t.Fatal("changed configuration")
			}
			if _, err := os.Lstat(target); !os.IsNotExist(err) {
				t.Fatal("created resource")
			}
		})
	}
}

func TestSymlinksAreRejected(t *testing.T) {
	for _, kind := range []string{"config", "config-parent", "resources", "local", "target", "payload-dir", "payload-file"} {
		t.Run(kind, func(t *testing.T) {
			original := []byte("ensure chat\n")
			config, resources, target := testServer(t, original)
			outside := t.TempDir()
			marker := filepath.Join(outside, "marker")
			if err := os.WriteFile(marker, []byte("preserve"), 0644); err != nil {
				t.Fatal(err)
			}
			link, destination := "", outside
			switch kind {
			case "config":
				link, destination = config+"-link", config
				config = link
			case "config-parent":
				link, destination = filepath.Join(outside, "server"), filepath.Dir(config)
				config = filepath.Join(link, "server.cfg")
			case "resources":
				link, destination = resources+"-link", resources
				resources = link
			case "local":
				link = filepath.Dir(target)
			case "target":
				if err := os.Mkdir(filepath.Dir(target), 0755); err != nil {
					t.Fatal(err)
				}
				link = target
			case "payload-dir", "payload-file":
				if err := os.MkdirAll(target, 0755); err != nil {
					t.Fatal(err)
				}
				link = filepath.Join(target, "lib")
				if kind == "payload-file" {
					link, destination = filepath.Join(target, "server.lua"), marker
				}
			}
			if err := os.Symlink(destination, link); err != nil {
				t.Skipf("symlinks unavailable: %v", err)
			}
			if err := install(config, resources, testPayload()); err == nil {
				t.Fatal("accepted symbolic link")
			}
			if !bytes.Equal(read(t, config), original) || len(backups(t, config)) != 0 {
				t.Fatal("changed configuration through symbolic link")
			}
			if string(read(t, marker)) != "preserve" {
				t.Fatal("changed external file")
			}
		})
	}
}

func TestExtractionFailureLeavesConfigurationUntouched(t *testing.T) {
	original := []byte("ensure chat\n")
	config, resources, target := testServer(t, original)
	blocker := filepath.Dir(target)
	if err := os.WriteFile(blocker, []byte("user file"), 0644); err != nil {
		t.Fatal(err)
	}
	if err := install(config, resources, testPayload()); err == nil {
		t.Fatal("expected extraction failure")
	}
	if !bytes.Equal(read(t, config), original) || len(backups(t, config)) != 0 {
		t.Fatal("modified configuration on extraction failure")
	}
	if !strings.Contains(string(read(t, blocker)), "user file") {
		t.Fatal("changed user file")
	}
}

func TestConfigurationFailureRollsBackOnlyNewResource(t *testing.T) {
	for _, preexisting := range []bool{false, true} {
		name := "new-resource"
		if preexisting {
			name = "existing-resource"
		}
		t.Run(name, func(t *testing.T) {
			original := []byte("ensure chat\n")
			config, resources, target := testServer(t, original)
			payload := testPayload()
			if preexisting {
				if err := os.MkdirAll(filepath.Join(target, "lib"), 0755); err != nil {
					t.Fatal(err)
				}
				for name, content := range payload {
					if err := os.WriteFile(filepath.Join(target, filepath.FromSlash(name)), content, 0644); err != nil {
						t.Fatal(err)
					}
				}
			}
			// A valid long filename leaves insufficient filename space for the
			// required unique backup, producing a real failure after extraction.
			longConfig := filepath.Join(filepath.Dir(config), strings.Repeat("c", 240)+".cfg")
			if err := os.Rename(config, longConfig); err != nil {
				t.Skipf("filesystem does not support the test filename: %v", err)
			}
			if err := install(longConfig, resources, payload); err == nil {
				t.Fatal("expected backup filename failure")
			}
			if !bytes.Equal(read(t, longConfig), original) {
				t.Fatal("configuration changed without a backup")
			}
			if preexisting {
				for name, content := range payload {
					if !bytes.Equal(read(t, filepath.Join(target, filepath.FromSlash(name))), content) {
						t.Fatal("preexisting resource changed")
					}
				}
			} else if _, err := os.Lstat(target); !os.IsNotExist(err) {
				t.Fatal("new resource was not rolled back")
			}
			entries, err := os.ReadDir(filepath.Dir(config))
			if err != nil {
				t.Fatal(err)
			}
			for _, entry := range entries {
				if strings.HasPrefix(entry.Name(), ".hwidlab-") {
					t.Fatal("temporary configuration left behind")
				}
			}
		})
	}
}
