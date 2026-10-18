package main

import (
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"math/big"
	"os"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"
	"time"
	"unicode/utf8"
)

const maxDatabaseBytes = 128 << 20

var banIDPattern = regexp.MustCompile(`^B[123456789ABCDEFGHJKLMNPQRSTUVWXYZ]{3}-[123456789ABCDEFGHJKLMNPQRSTUVWXYZ]{4}$`)

// BanInfo describes exactly one active ban in a locally selected txAdmin v5 database.
type BanInfo struct {
	ID, Reason, Target, DatabaseSHA256 string
}

type databaseSnapshot struct {
	path string
	data []byte
	info os.FileInfo
}

type selectedBan struct {
	info       BanInfo
	root       map[string]json.RawMessage
	actions    []json.RawMessage
	index      int
	action     map[string]json.RawMessage
	revocation map[string]json.RawMessage
}

// inspectBan never writes to the selected file. The caller must stop FXServer/txAdmin
// before revokeBan; these portable database functions do not manage server processes.
func inspectBan(path, banID string) (BanInfo, error) {
	snapshot, err := readDatabase(path)
	if err != nil {
		return BanInfo{}, err
	}
	ban, err := selectBan(snapshot.data, banID, time.Now().Unix())
	if err != nil {
		return BanInfo{}, err
	}
	return ban.info, nil
}

// revokeBan follows txAdmin's DAO: set revocation.timestamp/author, never delete an
// action or change identifiers. The confirmation hash prevents stale confirmations.
func revokeBan(path, banID, expectedSHA256 string) (backupPath string, err error) {
	snapshot, err := readDatabase(path)
	if err != nil {
		return "", err
	}
	if len(expectedSHA256) != 64 || databaseHash(snapshot.data) != expectedSHA256 {
		return "", fmt.Errorf("o banco mudou desde a confirmação; selecione e confira o ban novamente")
	}
	now := time.Now().Unix()
	ban, err := selectBan(snapshot.data, banID, now)
	if err != nil {
		return "", err
	}
	// Serialize this utility's own instances. This does not lock out txAdmin;
	// the stopped-server requirement remains necessary.
	lockPath := snapshot.path + ".local-unban.lock"
	lock, err := os.OpenFile(lockPath, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
	if err != nil {
		return "", fmt.Errorf("operação bloqueada: outra instância pode estar usando este banco; %w", err)
	}
	if err := lock.Close(); err != nil {
		os.Remove(lockPath)
		return "", err
	}
	defer os.Remove(lockPath)
	ban.revocation["timestamp"] = json.RawMessage(fmt.Sprintf("%d", now))
	ban.revocation["author"] = json.RawMessage(`"local-admin-tool"`)
	ban.action["revocation"], err = json.Marshal(ban.revocation)
	if err != nil {
		return "", err
	}
	ban.actions[ban.index], err = json.Marshal(ban.action)
	if err != nil {
		return "", err
	}
	ban.root["actions"], err = json.Marshal(ban.actions)
	if err != nil {
		return "", err
	}
	updated, err := json.MarshalIndent(ban.root, "", "  ")
	if err != nil {
		return "", err
	}
	updated = append(updated, '\n')
	if len(updated) > maxDatabaseBytes {
		return "", fmt.Errorf("o resultado excederia o limite de 128 MiB")
	}
	// Stage in the same directory so replacement never crosses filesystems.
	stage, err := writeExclusive(snapshot, ".local-unban-stage-*", updated)
	if err != nil {
		return "", fmt.Errorf("não foi possível preparar a gravação: %w", err)
	}
	defer os.Remove(stage)
	backupPattern := filepath.Base(snapshot.path) + ".local-unban-backup-" + time.Now().UTC().Format("20060102T150405.000000000Z") + "-*.json"
	backupPath, err = writeExclusive(snapshot, backupPattern, snapshot.data)
	if err != nil {
		return "", fmt.Errorf("backup não criado; banco preservado: %w", err)
	}
	// Verify the path, original identity and exact bytes immediately before rename.
	// This is an offline tool, not a lock shared with the running txAdmin process.
	fresh, err := readDatabase(snapshot.path)
	if err != nil {
		return backupPath, fmt.Errorf("banco preservado; falha ao conferir o original: %w", err)
	}
	if !os.SameFile(snapshot.info, fresh.info) || databaseHash(fresh.data) != expectedSHA256 {
		return backupPath, fmt.Errorf("o banco mudou durante a operação; nenhum ban foi alterado")
	}
	if err := os.Rename(stage, snapshot.path); err != nil {
		return backupPath, fmt.Errorf("não foi possível substituir o banco; original preservado: %w", err)
	}
	return backupPath, nil
}

func databaseHash(data []byte) string {
	sum := sha256.Sum256(data)
	return hex.EncodeToString(sum[:])
}

func readDatabase(path string) (databaseSnapshot, error) {
	absolute, err := filepath.Abs(path)
	if err != nil || strings.TrimSpace(path) == "" {
		return databaseSnapshot{}, fmt.Errorf("selecione o arquivo playersDB.json local")
	}
	if err := noDatabaseLinks(absolute); err != nil {
		return databaseSnapshot{}, err
	}
	file, err := os.Open(absolute)
	if err != nil {
		return databaseSnapshot{}, fmt.Errorf("não foi possível abrir o banco: %w", err)
	}
	defer file.Close()
	info, err := file.Stat()
	if err != nil {
		return databaseSnapshot{}, err
	}
	if !info.Mode().IsRegular() || info.Size() > maxDatabaseBytes {
		return databaseSnapshot{}, fmt.Errorf("o banco deve ser um arquivo regular de até 128 MiB")
	}
	data, err := io.ReadAll(io.LimitReader(file, maxDatabaseBytes+1))
	if err != nil {
		return databaseSnapshot{}, err
	}
	if len(data) > maxDatabaseBytes {
		return databaseSnapshot{}, fmt.Errorf("o banco excede o limite de 128 MiB")
	}
	if err := noDatabaseLinks(absolute); err != nil {
		return databaseSnapshot{}, err
	}
	current, err := os.Lstat(absolute)
	if err != nil || !os.SameFile(info, current) {
		return databaseSnapshot{}, fmt.Errorf("o arquivo do banco mudou durante a leitura")
	}
	return databaseSnapshot{absolute, data, info}, nil
}

func noDatabaseLinks(path string) error {
	for current := filepath.Clean(path); ; current = filepath.Dir(current) {
		info, err := os.Lstat(current)
		if err != nil {
			return fmt.Errorf("não foi possível verificar %s: %w", current, err)
		}
		// Go 1.23+ reports Windows junctions/name-surrogate reparse points as irregular.
		if info.Mode()&(os.ModeSymlink|os.ModeIrregular) != 0 {
			return fmt.Errorf("links simbólicos e redirecionamentos não são permitidos: %s", current)
		}
		if filepath.Dir(current) == current {
			return nil
		}
	}
}

func writeExclusive(snapshot databaseSnapshot, pattern string, data []byte) (string, error) {
	if err := noDatabaseLinks(filepath.Dir(snapshot.path)); err != nil {
		return "", err
	}
	file, err := os.CreateTemp(filepath.Dir(snapshot.path), pattern)
	if err != nil {
		return "", err
	}
	name := file.Name()
	ok := false
	defer func() {
		file.Close()
		if !ok {
			os.Remove(name)
		}
	}()
	if err := file.Chmod(snapshot.info.Mode().Perm()); err != nil {
		return "", err
	}
	if _, err := io.Copy(file, bytes.NewReader(data)); err != nil {
		return "", err
	}
	if err := file.Sync(); err != nil {
		return "", err
	}
	if err := file.Close(); err != nil {
		return "", err
	}
	ok = true
	return name, nil
}

func selectBan(data []byte, input string, now int64) (selectedBan, error) {
	banID := strings.ToUpper(strings.TrimSpace(input))
	if !banIDPattern.MatchString(banID) {
		return selectedBan{}, fmt.Errorf("informe um ID de ban txAdmin no formato B123-4567")
	}
	if err := validateUniqueJSON(data); err != nil {
		return selectedBan{}, fmt.Errorf("JSON inválido ou ambíguo; banco preservado: %w", err)
	}
	var root map[string]json.RawMessage
	if err := json.Unmarshal(data, &root); err != nil || root == nil {
		return selectedBan{}, fmt.Errorf("o banco deve ser um objeto txAdmin versão 5")
	}
	if version, err := unixNumber(root["version"]); err != nil || version != 5 {
		return selectedBan{}, fmt.Errorf("somente o formato oficial txAdmin versão 5 é aceito")
	}
	var actions []json.RawMessage
	for _, field := range []string{"players", "actions", "whitelistApprovals", "whitelistRequests"} {
		raw := bytes.TrimSpace(root[field])
		var array []json.RawMessage
		if len(raw) == 0 || raw[0] != '[' || json.Unmarshal(raw, &array) != nil {
			return selectedBan{}, fmt.Errorf("banco txAdmin inválido: %s deve ser uma lista", field)
		}
		if field == "actions" {
			actions = array
		}
	}
	index := -1
	var selected map[string]json.RawMessage
	for i, raw := range actions {
		var action map[string]json.RawMessage
		var id string
		if json.Unmarshal(raw, &action) != nil || action == nil || json.Unmarshal(action["id"], &id) != nil || id == "" {
			return selectedBan{}, fmt.Errorf("banco inválido: ação sem ID válido")
		}
		if id == banID {
			if index >= 0 {
				return selectedBan{}, fmt.Errorf("ID de ban duplicado; banco preservado")
			}
			index, selected = i, action
		}
	}
	if index < 0 {
		return selectedBan{}, fmt.Errorf("ban %s não encontrado neste banco", banID)
	}
	var actionType, reason, author string
	if json.Unmarshal(selected["type"], &actionType) != nil || actionType != "ban" {
		return selectedBan{}, fmt.Errorf("a ação selecionada não é um ban")
	}
	if json.Unmarshal(selected["reason"], &reason) != nil || reason == "" || json.Unmarshal(selected["author"], &author) != nil || author == "" {
		return selectedBan{}, fmt.Errorf("ban inválido: motivo ou autor ausente")
	}
	if _, err := unixNumber(selected["timestamp"]); err != nil {
		return selectedBan{}, fmt.Errorf("ban inválido: timestamp ausente ou inválido")
	}
	ids, err := stringList(selected["ids"])
	if err != nil || len(ids) == 0 {
		return selectedBan{}, fmt.Errorf("ban inválido: lista de identificadores ausente ou inválida")
	}
	if raw, exists := selected["hwids"]; exists {
		if _, err := stringList(raw); err != nil {
			return selectedBan{}, fmt.Errorf("ban inválido: lista de hwids inválida")
		}
	}
	target := strings.Join(ids, ", ")
	if !bytes.Equal(bytes.TrimSpace(selected["playerName"]), []byte("false")) {
		var name string
		if json.Unmarshal(selected["playerName"], &name) != nil || name == "" {
			return selectedBan{}, fmt.Errorf("ban inválido: playerName deve ser texto ou false")
		}
		target = name + " (" + target + ")"
	}
	if !bytes.Equal(bytes.TrimSpace(selected["expiration"]), []byte("false")) {
		expiration, err := unixNumber(selected["expiration"])
		if err != nil {
			return selectedBan{}, fmt.Errorf("ban inválido: expiration deve ser timestamp ou false")
		}
		if expiration <= now {
			return selectedBan{}, fmt.Errorf("este ban já expirou; nenhuma alteração necessária")
		}
	}
	var revocation map[string]json.RawMessage
	if json.Unmarshal(selected["revocation"], &revocation) != nil || revocation == nil {
		return selectedBan{}, fmt.Errorf("ban inválido: revocation ausente ou inválida")
	}
	for _, field := range []string{"timestamp", "author"} {
		raw, exists := revocation[field]
		if !exists {
			return selectedBan{}, fmt.Errorf("ban inválido: revocation.%s ausente", field)
		}
		if !bytes.Equal(bytes.TrimSpace(raw), []byte("null")) {
			return selectedBan{}, fmt.Errorf("este ban já está revogado ou contém revogação inconsistente")
		}
	}
	return selectedBan{BanInfo{banID, reason, target, databaseHash(data)}, root, actions, index, selected, revocation}, nil
}

func stringList(raw json.RawMessage) ([]string, error) {
	var list []string
	trimmed := bytes.TrimSpace(raw)
	if len(trimmed) == 0 || trimmed[0] != '[' || json.Unmarshal(raw, &list) != nil {
		return nil, fmt.Errorf("esperada lista de textos")
	}
	// encoding/json accepts JSON null in a string slice; require each actual token to be a string.
	var values []json.RawMessage
	if err := json.Unmarshal(raw, &values); err != nil {
		return nil, err
	}
	for _, value := range values {
		if len(bytes.TrimSpace(value)) == 0 || bytes.TrimSpace(value)[0] != '"' {
			return nil, fmt.Errorf("identificador deve ser texto")
		}
	}
	return list, nil
}

func unixNumber(raw json.RawMessage) (int64, error) {
	value := bytes.TrimSpace(raw)
	if len(value) == 0 || len(value) > 128 || (value[0] != '-' && (value[0] < '0' || value[0] > '9')) {
		return 0, fmt.Errorf("esperado número inteiro")
	}
	// Bound work before big.Rat exponent expansion on an untrusted local file.
	if exponentAt := bytes.IndexAny(value, "eE"); exponentAt >= 0 {
		exponent, err := strconv.ParseInt(string(value[exponentAt+1:]), 10, 32)
		if err != nil || exponent < -128 || exponent > 128 {
			return 0, fmt.Errorf("expoente de timestamp inválido")
		}
	}
	// Exact rational parsing also accepts valid JSON scientific notation without
	// rounding identifiers or large unknown numeric fields through float64.
	number, ok := new(big.Rat).SetString(string(value))
	if !ok || !number.IsInt() || !number.Num().IsInt64() || number.Sign() < 0 {
		return 0, fmt.Errorf("timestamp inválido")
	}
	return number.Num().Int64(), nil
}

// Reject duplicates at every depth: encoding/json alone silently keeps the last
// occurrence, which is unsafe when confirming a revocation or preserving records.
func validateUniqueJSON(data []byte) error {
	if !utf8.Valid(data) {
		return fmt.Errorf("JSON não contém UTF-8 válido")
	}
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.UseNumber()
	var walk func(int) error
	walk = func(depth int) error {
		if depth > 256 {
			return fmt.Errorf("JSON excede a profundidade suportada")
		}
		token, err := decoder.Token()
		if err != nil {
			return err
		}
		delimiter, container := token.(json.Delim)
		if !container {
			return nil
		}
		switch delimiter {
		case '{':
			keys := make(map[string]bool)
			for decoder.More() {
				keyToken, err := decoder.Token()
				if err != nil {
					return err
				}
				key, ok := keyToken.(string)
				if !ok || keys[key] {
					return fmt.Errorf("chave duplicada ou inválida: %q", key)
				}
				keys[key] = true
				if err := walk(depth + 1); err != nil {
					return err
				}
			}
		case '[':
			for decoder.More() {
				if err := walk(depth + 1); err != nil {
					return err
				}
			}
		default:
			return fmt.Errorf("delimitador inesperado")
		}
		_, err = decoder.Token()
		return err
	}
	if err := walk(0); err != nil {
		return err
	}
	if _, err := decoder.Token(); err != io.EOF {
		return fmt.Errorf("conteúdo adicional após o JSON")
	}
	return nil
}
