# sys 모듈과 sys.path 테스트
import sys

print("=== sys 모듈 정보 ===")
print("sys.platform:", sys.platform)
print("sys.version:", sys.version)
print("sys.executable:", sys.executable)

print("\n=== sys.path 정보 ===")
print("sys.path의 길이:", len(sys.path))
print("sys.path 내용:")
for i, path in enumerate(sys.path):
    print(f"  {i}: {path}")

print("\n=== sys.modules 정보 ===")
print("로드된 모듈 수:", len(sys.modules))
print("로드된 모듈들:")
for name in sys.modules:
    print(f"  - {name}")

print("\n=== 기타 sys 속성들 ===")
print("sys.maxsize:", sys.maxsize)
print("sys.byteorder:", sys.byteorder)
print("sys.getdefaultencoding():", sys.getdefaultencoding())

print("\n=== sys.path 조작 테스트 ===")
original_length = len(sys.path)
sys.path.append("/test/path")
print("경로 추가 후 길이:", len(sys.path))
print("마지막 경로:", sys.path[-1])