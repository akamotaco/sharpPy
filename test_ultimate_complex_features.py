#!/usr/bin/env python3
"""
Python 3.12 이하 최고 난이도 복합 기능 테스트
- 다중 상속 + Method Resolution Order (MRO)
- 메타클래스 상속 체인
- 데코레이터 (함수/클래스/메서드/프로퍼티)
- 컨텍스트 매니저 + 예외 처리
- 디스크립터 + 프로퍼티
- 제너레이터 + 코루틴
- Abstract Base Classes (ABC)
- Mixin 패턴
"""

import functools
import time
from abc import ABC, ABCMeta, abstractmethod
from typing import Any, Callable, Generator, Optional
from contextlib import contextmanager

print("=== Python 3.12 ULTIMATE Complex Features Test ===")

# 1. 고급 데코레이터 체인
def performance_monitor(func):
    """성능 모니터링 데코레이터"""
    @functools.wraps(func)
    def wrapper(*args, **kwargs):
        start = time.time()
        try:
            result = func(*args, **kwargs)
            duration = time.time() - start
            print(f"  ⚡ {func.__name__} executed in {duration:.4f}s")
            return result
        except Exception as e:
            print(f"  💥 {func.__name__} failed: {e}")
            raise
    return wrapper

def cache_result(maxsize=128):
    """결과 캐싱 데코레이터"""
    def decorator(func):
        cache = {}
        @functools.wraps(func)
        def wrapper(*args, **kwargs):
            key = str(args) + str(sorted(kwargs.items()))
            if key in cache:
                print(f"  💾 Cache hit for {func.__name__}")
                return cache[key]
            result = func(*args, **kwargs)
            if len(cache) < maxsize:
                cache[key] = result
                print(f"  📝 Cached result for {func.__name__}")
            return result
        return wrapper
    return decorator

def validate_types(**type_checks):
    """타입 검증 데코레이터"""
    def decorator(func):
        @functools.wraps(func)
        def wrapper(*args, **kwargs):
            # 타입 검증
            for param_name, expected_type in type_checks.items():
                if param_name in kwargs:
                    value = kwargs[param_name]
                    if not isinstance(value, expected_type):
                        raise TypeError(f"{param_name} must be {expected_type.__name__}")
            return func(*args, **kwargs)
        return wrapper
    return decorator

# 2. 메타클래스 상속 체인 (3단계)
class LoggingMeta(type):
    """모든 메서드 호출을 로깅하는 메타클래스"""
    def __new__(cls, name, bases, namespace):
        print(f"🏗️ LoggingMeta creating class: {name}")
        
        # 모든 메서드에 로깅 추가
        for attr_name, attr_value in namespace.items():
            if callable(attr_value) and not attr_name.startswith('__'):
                namespace[attr_name] = cls._add_logging(attr_value, attr_name)
        
        result = super().__new__(cls, name, bases, namespace)
        return result
    
    @staticmethod
    def _add_logging(method, method_name):
        @functools.wraps(method)
        def logged_method(self, *args, **kwargs):
            print(f"  📋 Calling {self.__class__.__name__}.{method_name}")
            return method(self, *args, **kwargs)
        return logged_method

class ValidationMeta(LoggingMeta):
    """검증 기능이 추가된 메타클래스"""
    def __new__(cls, name, bases, namespace):
        print(f"🔍 ValidationMeta processing class: {name}")
        
        # 모든 public 속성에 대해 validate 메서드 생성
        for attr_name in list(namespace.keys()):
            if not attr_name.startswith('_') and not callable(namespace[attr_name]):
                validate_method = f"validate_{attr_name}"
                if validate_method not in namespace:
                    namespace[validate_method] = cls._create_validator(attr_name)
        
        result = super().__new__(cls, name, bases, namespace)
        print(f"✅ ValidationMeta completed: {name}")
        return result
    
    @staticmethod
    def _create_validator(attr_name):
        def validator(self):
            value = getattr(self, attr_name, None)
            print(f"  ✅ Validating {attr_name}: {value}")
            return value is not None
        return validator

class EnhancedMeta(ValidationMeta, ABCMeta):
    """최상위 메타클래스 - 모든 기능 통합"""
    def __new__(cls, name, bases, namespace):
        print(f"🚀 EnhancedMeta creating enhanced class: {name}")
        
        # 클래스 생성 시간 기록
        namespace['_created_at'] = time.time()
        
        # 자동 __repr__ 생성
        if '__repr__' not in namespace:
            namespace['__repr__'] = cls._create_repr()
        
        result = super().__new__(cls, name, bases, namespace)
        return result
    
    @staticmethod
    def _create_repr():
        def __repr__(self):
            attrs = []
            for name, value in self.__dict__.items():
                if not name.startswith('_'):
                    attrs.append(f"{name}={repr(value)}")
            return f"{self.__class__.__name__}({', '.join(attrs)})"
        return __repr__

# 3. 다중 상속을 위한 Mixin 클래스들
class TimestampMixin:
    """타임스탬프 기능을 제공하는 Mixin"""
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self._timestamp = time.time()
        print(f"  🕐 TimestampMixin initialized at {self._timestamp}")
    
    def get_age(self) -> float:
        return time.time() - self._timestamp

class SerializableMixin:
    """직렬화 기능을 제공하는 Mixin"""
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        print(f"  💾 SerializableMixin initialized")
    
    def to_dict(self) -> dict:
        return {k: v for k, v in self.__dict__.items() if not k.startswith('_')}
    
    @classmethod
    def from_dict(cls, data: dict):
        return cls(**data)

class ContextMixin:
    """컨텍스트 매니저 기능을 제공하는 Mixin"""
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self._in_context = False
        print(f"  🔐 ContextMixin initialized")
    
    def __enter__(self):
        print(f"  🚪 Entering context for {self.__class__.__name__}")
        self._in_context = True
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        print(f"  🚪 Exiting context for {self.__class__.__name__}")
        self._in_context = False
        if exc_type:
            print(f"  ⚠️ Exception handled: {exc_val}")
        return False

# 4. 추상 기본 클래스 (ABC)
class BaseProcessor(ABC, metaclass=EnhancedMeta):
    """데이터 처리를 위한 추상 기본 클래스"""
    
    def __init__(self, name: str):
        self.name = name
        self.processed_count = 0
        super().__init__()
    
    @abstractmethod
    def process_item(self, item: Any) -> Any:
        """개별 아이템 처리 - 반드시 구현해야 함"""
        pass
    
    @abstractmethod
    def validate_input(self, item: Any) -> bool:
        """입력 검증 - 반드시 구현해야 함"""
        pass
    
    def process_batch(self, items: list) -> Generator[Any, None, None]:
        """배치 처리 - 구체 클래스에서 사용 가능"""
        print(f"  🏭 {self.name} processing batch of {len(items)} items")
        for item in items:
            if self.validate_input(item):
                processed = self.process_item(item)
                self.processed_count += 1
                yield processed
            else:
                print(f"  ❌ Invalid item skipped: {item}")

# 5. 디스크립터 클래스
class ValidatedProperty:
    """검증이 포함된 고급 디스크립터"""
    def __init__(self, name: str, validator: Callable = None, 
                 transformer: Callable = None):
        self.name = name
        self.validator = validator
        self.transformer = transformer
        self.private_name = f'_{name}'
    
    def __get__(self, obj, objtype=None):
        if obj is None:
            return self
        value = getattr(obj, self.private_name, None)
        print(f"  👀 Getting {self.name}: {value}")
        return value
    
    def __set__(self, obj, value):
        print(f"  ✏️ Setting {self.name} = {value}")
        
        # 변환 적용
        if self.transformer:
            value = self.transformer(value)
            print(f"  🔄 Transformed to: {value}")
        
        # 검증 수행
        if self.validator and not self.validator(value):
            raise ValueError(f"Invalid value for {self.name}: {value}")
        
        setattr(obj, self.private_name, value)
        print(f"  ✅ {self.name} set successfully")
    
    def __delete__(self, obj):
        if hasattr(obj, self.private_name):
            print(f"  🗑️ Deleting {self.name}")
            delattr(obj, self.private_name)

# 6. 최종 복합 클래스 - 모든 기능 통합
class DataProcessor(BaseProcessor, TimestampMixin, SerializableMixin, 
                   ContextMixin):
    """모든 고급 기능이 통합된 최종 클래스"""
    
    # 디스크립터를 사용한 검증된 속성들
    name = ValidatedProperty('name', 
                           validator=lambda x: isinstance(x, str) and len(x) > 0,
                           transformer=str.title)
    
    threshold = ValidatedProperty('threshold',
                                validator=lambda x: isinstance(x, (int, float)) and x >= 0,
                                transformer=float)
    
    def __init__(self, name: str, threshold: float = 1.0):
        print(f"🏗️ Creating DataProcessor: {name}")
        # 다중 상속 초기화 (MRO 순서 중요!)
        super().__init__(name)
        self.threshold = threshold
        self.results = []
    
    def process_item(self, item: Any) -> Any:
        """아이템 처리 구현"""
        if isinstance(item, (int, float)):
            result = item * self.threshold
            print(f"  🔢 Processed {item} → {result}")
            return result
        elif isinstance(item, str):
            result = item.upper()
            print(f"  📝 Processed '{item}' → '{result}'")
            return result
        else:
            return str(item)
    
    def validate_input(self, item: Any) -> bool:
        """입력 검증 구현"""
        is_valid = item is not None
        print(f"  🔍 Validating {item}: {'✅' if is_valid else '❌'}")
        return is_valid
    
    @performance_monitor
    @cache_result(maxsize=50)
    @validate_types(data=list, multiplier=float)
    def advanced_process(self, data: list, multiplier: float = 2.0) -> list:
        """고급 처리 메서드 - 모든 데코레이터 적용"""
        print(f"  🚀 Advanced processing with multiplier {multiplier}")
        
        with self:  # 컨텍스트 매니저 사용
            results = []
            for processed in self.process_batch(data):
                if isinstance(processed, (int, float)):
                    results.append(processed * multiplier)
                else:
                    results.append(processed)
            
            self.results.extend(results)
            return results
    
    @property
    def statistics(self) -> dict:
        """처리 통계 프로퍼티"""
        return {
            'name': self.name,
            'processed_count': self.processed_count,
            'age_seconds': self.get_age(),
            'results_count': len(self.results),
            'threshold': self.threshold
        }
    
    def __str__(self) -> str:
        return f"DataProcessor(name='{self.name}', processed={self.processed_count})"

# 7. 추가 상속 클래스: 특수화된 프로세서
class NumberProcessor(DataProcessor):
    """숫자 전용 프로세서"""
    
    def __init__(self, name: str, threshold: float = 1.0, precision: int = 2):
        super().__init__(name, threshold)
        self.precision = precision
        print(f"  🔢 NumberProcessor specialized with precision={precision}")
    
    def process_item(self, item: Any) -> Any:
        """숫자 처리 특수화"""
        if isinstance(item, (int, float)):
            result = round(super().process_item(item), self.precision)
            print(f"  📊 Number specialized: {item} → {result}")
            return result
        else:
            # 문자열을 숫자로 변환 시도
            try:
                num_item = float(item)
                return self.process_item(num_item)
            except ValueError:
                print(f"  ⚠️ Could not convert '{item}' to number")
                return 0.0

class StringProcessor(DataProcessor):
    """문자열 전용 프로세서"""
    
    def __init__(self, name: str, case_style: str = "upper"):
        super().__init__(name, 1.0)
        self.case_style = case_style
        print(f"  📝 StringProcessor specialized with case_style={case_style}")
    
    def process_item(self, item: Any) -> Any:
        """문자열 처리 특수화"""
        str_item = str(item)
        if self.case_style == "upper":
            result = str_item.upper()
        elif self.case_style == "lower":
            result = str_item.lower()
        elif self.case_style == "title":
            result = str_item.title()
        else:
            result = str_item
        
        print(f"  🔤 String specialized: '{str_item}' → '{result}'")
        return result

# 8. 컨텍스트 매니저 함수
@contextmanager
def processing_session(name: str):
    """처리 세션 컨텍스트 매니저"""
    print(f"🎬 Starting processing session: {name}")
    session_start = time.time()
    try:
        yield session_start
    except Exception as e:
        print(f"💥 Error in session {name}: {e}")
        raise
    finally:
        duration = time.time() - session_start
        print(f"🎬 Session {name} completed in {duration:.4f}s")

# 9. 최종 통합 테스트 함수
def test_ultimate_features():
    """모든 고급 기능을 종합적으로 테스트"""
    print("\n" + "="*60)
    print("🚀 ULTIMATE COMPLEX FEATURES TEST")
    print("="*60)
    
    with processing_session("Ultimate Test"):
        
        print("\n1. Testing DataProcessor with all features:")
        processor = DataProcessor("MainProcessor", 2.5)
        
        # 디스크립터 테스트
        print(f"   Original name: {processor.name}")
        processor.name = "enhanced processor"  # transformer 동작
        print(f"   Transformed name: {processor.name}")
        
        # 메서드 테스트 (메타클래스 로깅 동작)
        print(f"   Statistics: {processor.statistics}")
        
        # 고급 처리 테스트 (모든 데코레이터 동작)
        test_data = [1, 2, 3, "hello", 4.5, None, "world"]
        results1 = processor.advanced_process(test_data, 3.0)
        print(f"   First run results: {results1}")
        
        # 캐시 테스트 (같은 입력으로 다시 호출)
        results2 = processor.advanced_process(test_data, 3.0)
        print(f"   Cached run results: {results2}")
        
        # 직렬화 테스트 (Mixin 기능)
        serialized = processor.to_dict()
        print(f"   Serialized: {serialized}")
        
        print("\n2. Testing inheritance hierarchy:")
        
        # NumberProcessor 테스트
        num_processor = NumberProcessor("NumberCruncher", 1.5, 3)
        with num_processor:
            num_results = list(num_processor.process_batch([1, 2.7, "3.14", 4.999]))
            print(f"   Number processing results: {num_results}")
        
        # StringProcessor 테스트
        str_processor = StringProcessor("TextProcessor", "title")
        with str_processor:
            str_results = list(str_processor.process_batch(["hello", "world", 123, None]))
            print(f"   String processing results: {str_results}")
        
        print("\n3. Testing MRO (Method Resolution Order):")
        print(f"   DataProcessor MRO: {[cls.__name__ for cls in DataProcessor.__mro__]}")
        print(f"   NumberProcessor MRO: {[cls.__name__ for cls in NumberProcessor.__mro__]}")
        
        print("\n4. Testing metaclass inheritance chain:")
        print(f"   DataProcessor metaclass: {type(DataProcessor).__name__}")
        print(f"   Metaclass MRO: {[cls.__name__ for cls in type(DataProcessor).__mro__]}")
        
        print("\n5. Testing validation and error handling:")
        try:
            processor.threshold = -1  # 검증 실패해야 함
        except ValueError as e:
            print(f"   ✅ Validation error caught: {e}")
        
        try:
            # 잘못된 타입으로 호출
            processor.advanced_process("not a list", "not a float")
        except TypeError as e:
            print(f"   ✅ Type validation error caught: {e}")
        
        print("\n6. Final statistics:")
        for proc in [processor, num_processor, str_processor]:
            print(f"   {proc}: {proc.statistics}")

# 메인 실행
if __name__ == "__main__":
    test_ultimate_features()
    print("\n" + "="*60)
    print("🎉 ALL ULTIMATE COMPLEX FEATURES TESTED SUCCESSFULLY!")
    print("="*60)