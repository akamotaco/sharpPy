# 129차: 파일 열기와 버퍼 경계

후속 6번. 고정 CPython 3.12.15 Modules/_io의 open → FileIO → buffered/text
계층을 기준으로, with 진입 때만 열던 기존 facade를 수정했다.

- open 호출 시 즉시 파일을 연다. __enter__는 같은 객체를 반환하고 재개방하지 않는다.
- r/w/a/x, binary/text, update 모드를 구분한다. w는 실제 truncate, a는 끝에 쓴다.
- binary read/write, bytearray/연속 memoryview 쓰기, read(size), 텍스트 Unicode 문자 수,
  universal newline, readlines의 마지막 개행 보존을 구현했다.
- open 인자 중복/타입/PathLike를 검사하고 누락 파일·독점 생성 오류를 errno와 filename으로 전달한다.
- close/flush 오류를 삼키지 않고, 실패하더라도 closed 상태와 stream 정리를 수행한다.
- Godot res/user 경로는 FileAccess를 Stream으로 감싼 소스 파일을 사용한다. 별도 DLL이 없다.

전체 텍스트 읽기는 필요할 때 byte buffer를 확보해 직접 decode한다. 부분 읽기를 한
경우에는 기존 decoder 상태를 계속 쓴다. 최초 파일 길이는 용량 힌트이며 EOF를
추가 확인하여 성장/축소를 처리한다. read-only text는 FileStream 중복 버퍼를 없앴다.
메서드 descriptor는 공유한다. 초기 후보의 시간 지연과 버퍼 확대의 할당 증가를
확인한 뒤 이 경로를 채택했다.

## 검증과 측정

기본 230개: 출력 일치 225, 가변 필드 제외 3, native CLI 2.
관련 2개는 최종 코드에서 CPython과 일치한다. Godot 소스 빌드, 기본 17개와 경로
5개, 관련 2개 통과. fixture는 Godot에서 user://에 쓰며 rb 회귀는 res://를 읽는다.
호스트 fault injection은 flush/close 각각 오류 전파, closed, Dispose를 확인했다.

8KB 텍스트 파일 open/read/close 1,000회, samples 15, 독립 3쌍,
affinity 0x4, tiering off에서 22.3696 → 21.5429ms (약 3.7% 단축).
할당은 약 41.818MB → 25.706MB (약 38.5% 감소). 정수 loop 대조군 시간 범위는
겹친다. 모든 IO 연산의 성능 개선을 뜻하지 않는다. baseline은 125차로 이후
set/str.format 변경은 이 workload에 해당하지 않는다. 원자료 round129-validation.json.

compatibility 전체 검사는 최종 read-all/descriptor 최적화 전 코드에서 76개 중
73개 일치, memoryview 2개 및 새 str subtype 1개가 실패/차이였다. 최종 IO 회귀는
다시 실행했다. 기존 binary open 실패는 해결되어 기본 회귀에 등록했다.

## 남는 범위

완전한 _io 구현 완료로 분류하지 않는다. fd/opener, 모든 buffering 옵션,
TextIOWrapper의 opaque tell cookie와 혼합 read/write, codec 오류 객체/등록된
error handler, IOBase 타입 계층·UnsupportedOperation 및 전체 API는 남는다.
Godot FileAccess는 독점 생성 primitive를 제공하지 않아 x의 존재 검사와 생성은
원자적이지 않다. CLR 배열/문자열 크기를 넘는 입력에는 플랫폼 한계가 있다.
