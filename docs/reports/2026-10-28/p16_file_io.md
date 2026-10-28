# p16 — 파일 I/O parity (2026-10-28)

## 스코프
`tests/bughunt/p16_file_io.py` 신규 프로브: open 모드(r/w/a/x, b/t, +), 이터레이션,
tell/seek(cookie, whence), truncate, writelines, closed 파일, encoding别名
(latin-1/ascii/utf-16), os.remove/rename 실패 보고. 기준: CPython 3.12.15 (Windows).

## 발견 → 수정 (11건)
1. **파일 이터레이션 불가** — `'TextIOWrapper' object is not iterable` →
   `GetIterator()/Next()` override (line 단위, EOF에서 StopIteration),
   `__iter__`/`__next__` 속성 노출.
2. **text tell()** — raw 바이트 위치 대신 CPython과 동일한 **디코딩 문자 수**를
   추적(`textPos`). read/write 양쪽에서 갱신, seek(재규정) 시 리셋.
3. **text seek** — cookie → 문자 오프셋 재현: stream 0 리셋 후 offset개 문자
   fast-forward, whence=2는 EOF까지 소진. `seek()`는 새 위치 반환(이 빌드의
   CPython 측정값과 일치).
4. **잘못된 seek 대상(binary)** — CLR IOException 누출 → CPython과 같은
   `OSError: [Errno 22] Invalid argument`.
5. **whence 범위 오류 문구** — `invalid whence (...)` → `whence value 5 unsupported`.
6. **모드 위반** — `read of write-only` / `write of read-only`가 plain OSError였던
   것을 **`io.UnsupportedOperation`(OSError+ValueError MRO)** +
   `not readable`/`not writable`로. 신규 `PyUnsupportedOperation` 타입.
7. **encoding 별칭** — `latin-1` 등이 `LookupError` → CPython codecs 별칭 테이블
   (latin-1/latin/iso-8859-1/cp819, us-ascii/646, utf16→utf-16LE, utf-8-sig).
8. **truncate() 없음** → 구현: 인자 = raw 바이트 오프셋(CPython `IOBase.truncate`
   의미), 미지원 모드면 UnsupportedOperation.
9. **writelines() 없음** → 구현: 임의 iterable, 항목별 write 위임(타입 오류 전파).
10. **os.remove 미존재 파일이 조용히 None** → `FileNotFoundError`,
    CPython Windows 형식 `[WinError 2] 지정된 파일을 찾을 수 없습니다: 'path'`
    (FormatMessage 말미 '.' trim 포함). `os.rename`도 WinError 2/3/5/183 매핑.
    Godot 빌드는 기존 POSIX식 `[Errno n]` 문구를 유지하도록 `#if GODOT` 분리.
11. **임시 open() 객체의 쓰기 가시성** — CPython은 refcount 0 시점에 자동
    close/flush하지만 SharpPy는 GC 미확정 → `open(f,'w').write(...)` 후 재읽기가
    ''였음. write/writelines 시 스트림 flush로 관측 일치(성능 트레이드오프 명시).

## host별 동작 분리
- .NET(dotnet) host: Windows OS 관측 그대로 — `[WinError n] <로컬라이즈 메시지>`.
- Godot host: `platform/helper_godot.cs` 경로 + `[Errno n]` 스타일 유지(호스트 OS가
  가상 FS에 이식되는 형태). `NtModule.WindowsOr()` 한 곳에서 분기.

## 검증
- p16 stdout diff **0**.
- `tools/run_bughunt.py`: **23/23 matching**.
- `tools/evaluate.py`: 변화 없음 `225 + 3 + 2`.
- perf probe: 플래그 없음(str_fmt -15.7% 개선 포함 전부 노이즈 대).
- Godot 4.7.2 mono: CompatibilityProbe Debug/Release 컴파일 클린.

## 알려진 한계 / 후속
- text 모드에서 버퍼 미flush 상태에서 tell()은 문자 논리 위치, truncate() 무인자는
  raw 기록 위치 사용(ASCII/소규모 케이스 관측 일치, 다바이트+버퍼 조합은 근사).
- seek past-EOF cookie는 EOF로 정규화(CPython과 동일 관측).
- **Godot default probe 회귀 아님(별건)**: `slice_delete_compaction_regression.py`
  が Debug 빌드의 DEBUG 전용 명령어 가드(60k)를 초과. HEAD(p15, p16 변경 없음)에서도
  동일 초과 확인 → p16 회귀 아님. Release에서는 0.49s, 1440 case 정답.
  가드 상향은 금지 지침에 따라 보류; 명령어 수 증가분이 p13~p15 어디에서 발생했는지
  단독 조사 항목으로 남긴다.
