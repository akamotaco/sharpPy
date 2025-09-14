#!/usr/bin/env python3
"""
Python 3.12 Advanced Features Comprehensive Test
- Property Decorator System (getter/setter/deleter) 
- Descriptor Protocol 심화
- Advanced Function Signatures
- Method Resolution & Attribute Access
- Iterator/Generator Advanced Patterns
- Exception Handling Advanced Features  
- Magic Methods Comprehensive
- Context Manager Advanced Patterns
- Type System Features
"""

print("=== Python 3.12 ADVANCED Features Test ===")

# 1. Property Decorator System (핵심 요청사항)
class PropertyDemo:
    """Property decorator의 getter/setter/deleter 종합 테스트"""
    
    def __init__(self):
        self._temperature = 0
        self._name = "Unknown"
        self._readonly_data = "Secret"
    
    @property
    def temperature(self):
        """Temperature getter with validation"""
        print(f"  📖 Getting temperature: {self._temperature}")
        return self._temperature
    
    @temperature.setter 
    def temperature(self, value):
        """Temperature setter with validation"""
        if not isinstance(value, (int, float)):
            raise TypeError("Temperature must be a number")
        if value < -273.15:
            raise ValueError("Temperature cannot be below absolute zero")
        print(f"  ✏️ Setting temperature: {value}")
        self._temperature = value
    
    @temperature.deleter
    def temperature(self):
        """Temperature deleter"""
        print("  🗑️ Deleting temperature")
        self._temperature = 0
    
    @property
    def name(self):
        """Name getter"""
        return self._name
    
    @name.setter
    def name(self, value):
        """Name setter with validation"""
        if not isinstance(value, str):
            raise TypeError("Name must be a string")
        if len(value.strip()) == 0:
            raise ValueError("Name cannot be empty")
        self._name = value.strip().title()
    
    @property  
    def readonly_data(self):
        """Read-only property (no setter)"""
        return self._readonly_data
    
    @property
    def computed_info(self):
        """Computed property based on other properties"""
        return f"{self.name} at {self.temperature}°C"

# 2. Custom Descriptor Protocol 심화
class ValidatedAttribute:
    """Custom descriptor with validation"""
    
    def __init__(self, validator=None, default=None):
        self.validator = validator
        self.default = default
        self.data = {}
    
    def __get__(self, obj, owner):
        if obj is None:
            return self
        return self.data.get(id(obj), self.default)
    
    def __set__(self, obj, value):
        if self.validator and not self.validator(value):
            raise ValueError(f"Invalid value: {value}")
        self.data[id(obj)] = value
    
    def __delete__(self, obj):
        self.data.pop(id(obj), None)

class TypedAttribute:
    """Type-checked descriptor"""
    
    def __init__(self, expected_type):
        self.expected_type = expected_type
        self.data = {}
    
    def __get__(self, obj, owner):
        if obj is None:
            return self
        return self.data.get(id(obj))
    
    def __set__(self, obj, value):
        if not isinstance(value, self.expected_type):
            raise TypeError(f"Expected {self.expected_type.__name__}, got {type(value).__name__}")
        self.data[id(obj)] = value

class DescriptorDemo:
    """Descriptor protocol comprehensive test"""
    
    # Custom descriptors
    positive_number = ValidatedAttribute(lambda x: isinstance(x, (int, float)) and x > 0, 1)
    name = TypedAttribute(str)
    
    def __init__(self, name, number=5):
        self.name = name
        self.positive_number = number

# 3. Advanced Function Signatures
def complex_function_signature(
    pos_only_arg,           # positional-only
    /,                      # separator
    normal_arg,             # normal argument  
    default_arg="default",  # default value
    *args,                  # variable positional
    kw_only_arg,           # keyword-only
    kw_only_default="kw",  # keyword-only with default
    **kwargs               # variable keyword
):
    """Complex function signature demonstration"""
    return {
        'pos_only': pos_only_arg,
        'normal': normal_arg, 
        'default': default_arg,
        'args': args,
        'kw_only': kw_only_arg,
        'kw_default': kw_only_default,
        'kwargs': kwargs
    }

# 4. Method Resolution & Attribute Access
class AttributeAccessDemo:
    """Advanced attribute access control"""
    
    def __init__(self):
        self._data = {}
        self._access_log = []
    
    def __getattribute__(self, name):
        # 모든 attribute 접근을 로깅
        if name.startswith('_'):
            return super().__getattribute__(name)
        
        access_log = super().__getattribute__('_access_log')
        access_log.append(f"GET: {name}")
        
        try:
            return super().__getattribute__(name)
        except AttributeError:
            return super().__getattribute__('__getattr__')(name)
    
    def __getattr__(self, name):
        # 존재하지 않는 attribute 처리
        data = super().__getattribute__('_data')
        if name in data:
            return data[name]
        raise AttributeError(f"'{type(self).__name__}' object has no attribute '{name}'")
    
    def __setattr__(self, name, value):
        if name.startswith('_'):
            super().__setattr__(name, value)
        else:
            if not hasattr(self, '_access_log'):
                super().__setattr__('_access_log', [])
            self._access_log.append(f"SET: {name} = {value}")
            self._data[name] = value
    
    def __delattr__(self, name):
        if name.startswith('_'):
            super().__delattr__(name)
        else:
            self._access_log.append(f"DEL: {name}")
            if name in self._data:
                del self._data[name]
            else:
                raise AttributeError(f"'{type(self).__name__}' object has no attribute '{name}'")

# 5. Iterator/Generator Advanced Patterns  
class CustomIterator:
    """Custom iterator implementation"""
    
    def __init__(self, data):
        self.data = data
        self.index = 0
    
    def __iter__(self):
        return self
    
    def __next__(self):
        if self.index >= len(self.data):
            raise StopIteration
        value = self.data[self.index]
        self.index += 1
        return value

def fibonacci_generator(n):
    """Fibonacci generator with yield"""
    a, b = 0, 1
    for _ in range(n):
        yield a
        a, b = b, a + b

def delegating_generator():
    """Generator delegation with yield from"""
    yield from range(3)
    yield from fibonacci_generator(5) 
    yield from [100, 200, 300]

# 6. Exception Handling Advanced Features
class CustomError(Exception):
    """Custom exception with context"""
    
    def __init__(self, message, error_code=None, context=None):
        super().__init__(message)
        self.error_code = error_code
        self.context = context

class ValidationError(CustomError):
    """Validation-specific error"""
    pass

def exception_chain_demo():
    """Exception chaining demonstration"""
    try:
        try:
            raise ValueError("Original error")
        except ValueError as e:
            # Exception chaining with 'from'
            raise CustomError("Wrapped error", error_code=500) from e
    except CustomError as e:
        print(f"  💥 Caught: {e}")
        print(f"  📋 Error code: {e.error_code}")
        print(f"  🔗 Caused by: {e.__cause__}")
        return e

# 7. Magic Methods Comprehensive
class MathVector:
    """Vector class with full magic method implementation"""
    
    def __init__(self, *components):
        self.components = list(components)
    
    def __repr__(self):
        return f"Vector({', '.join(map(str, self.components))})"
    
    def __str__(self):
        return f"<{', '.join(map(str, self.components))}>"
    
    def __len__(self):
        return len(self.components)
    
    def __getitem__(self, index):
        return self.components[index]
    
    def __setitem__(self, index, value):
        self.components[index] = value
    
    def __add__(self, other):
        if isinstance(other, MathVector):
            if len(self) != len(other):
                raise ValueError("Vector dimensions must match")
            return MathVector(*(a + b for a, b in zip(self.components, other.components)))
        return MathVector(*(x + other for x in self.components))
    
    def __radd__(self, other):
        return self.__add__(other)
    
    def __mul__(self, scalar):
        return MathVector(*(x * scalar for x in self.components))
    
    def __rmul__(self, scalar):
        return self.__mul__(scalar)
    
    def __eq__(self, other):
        if not isinstance(other, MathVector):
            return False
        return self.components == other.components
    
    def __lt__(self, other):
        # Compare by magnitude
        return sum(x*x for x in self.components) < sum(x*x for x in other.components)
    
    def __bool__(self):
        return any(x != 0 for x in self.components)

# 8. Context Manager Advanced Patterns
class ResourceManager:
    """Advanced context manager with cleanup"""
    
    def __init__(self, resource_name, auto_cleanup=True):
        self.resource_name = resource_name
        self.auto_cleanup = auto_cleanup
        self.resource = None
        self.cleanup_callbacks = []
    
    def __enter__(self):
        print(f"  🔓 Acquiring resource: {self.resource_name}")
        self.resource = f"Handle_{self.resource_name}"
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        print(f"  🔒 Releasing resource: {self.resource_name}")
        
        # Run cleanup callbacks
        for callback in reversed(self.cleanup_callbacks):
            try:
                callback()
            except Exception as e:
                print(f"  ⚠️ Cleanup error: {e}")
        
        if exc_type is not None:
            print(f"  💥 Exception in context: {exc_type.__name__}: {exc_val}")
            return False  # Don't suppress exception
        
        self.resource = None
        return True
    
    def add_cleanup(self, callback):
        self.cleanup_callbacks.append(callback)

def multiple_context_managers():
    """Multiple context managers test"""
    with ResourceManager("Database") as db, ResourceManager("Cache") as cache:
        db.add_cleanup(lambda: print("    🧹 DB cleanup"))
        cache.add_cleanup(lambda: print("    🧹 Cache cleanup"))
        print(f"  📊 Using {db.resource} and {cache.resource}")
        return "Success"

# 9. Container Protocol Implementation  
class SmartContainer:
    """Container with full protocol implementation"""
    
    def __init__(self, initial_data=None):
        self._data = list(initial_data or [])
    
    def __len__(self):
        return len(self._data)
    
    def __getitem__(self, key):
        if isinstance(key, slice):
            return SmartContainer(self._data[key])
        return self._data[key]
    
    def __setitem__(self, key, value):
        self._data[key] = value
    
    def __delitem__(self, key):
        del self._data[key]
    
    def __contains__(self, item):
        return item in self._data
    
    def __iter__(self):
        return iter(self._data)
    
    def __reversed__(self):
        return reversed(self._data)
    
    def append(self, item):
        self._data.append(item)

# 10. Comprehensive Test Execution
def test_advanced_features():
    """Execute all advanced features tests"""
    
    print("\n🧪 1. Property Decorator System Tests")
    demo = PropertyDemo()
    
    # Test property getter
    print(f"Initial temperature: {demo.temperature}")
    
    # Test property setter
    demo.temperature = 25.5
    demo.name = "  alice  "
    print(f"After setting: {demo.computed_info}")
    
    # Test property setter validation
    try:
        demo.temperature = -300  # Should fail
    except ValueError as e:
        print(f"  ✅ Validation works: {e}")
    
    # Test property deleter
    del demo.temperature
    print(f"After deletion: {demo.temperature}")
    
    # Test readonly property  
    print(f"Readonly data: {demo.readonly_data}")
    
    print("\n🧪 2. Custom Descriptor Tests")
    desc_demo = DescriptorDemo("Test", 42)
    print(f"Descriptor values: {desc_demo.name}, {desc_demo.positive_number}")
    
    try:
        desc_demo.positive_number = -5  # Should fail
    except ValueError as e:
        print(f"  ✅ Descriptor validation: {e}")
    
    print("\n🧪 3. Complex Function Signature Tests")  
    result = complex_function_signature(
        1,                          # pos_only_arg
        "normal",                   # normal_arg  
        kw_only_arg="keyword",      # kw_only_arg
        extra_kw="extra"            # **kwargs
    )
    print(f"Function result: {result}")
    
    print("\n🧪 4. Attribute Access Control Tests")
    attr_demo = AttributeAccessDemo()
    attr_demo.dynamic_attr = "test"
    print(f"Dynamic attribute: {attr_demo.dynamic_attr}")
    
    del attr_demo.dynamic_attr
    print(f"Access log: {attr_demo._access_log}")
    
    print("\n🧪 5. Iterator/Generator Tests")
    custom_iter = CustomIterator([1, 2, 3])
    print(f"Custom iterator: {list(custom_iter)}")
    
    fib = list(fibonacci_generator(8))
    print(f"Fibonacci: {fib}")
    
    delegated = list(delegating_generator())
    print(f"Delegated generator: {delegated}")
    
    print("\n🧪 6. Exception Handling Tests")
    chained_error = exception_chain_demo()
    
    print("\n🧪 7. Magic Methods Tests")
    v1 = MathVector(1, 2, 3)
    v2 = MathVector(4, 5, 6) 
    v3 = v1 + v2
    v4 = v1 * 2
    print(f"Vector math: {v1} + {v2} = {v3}")
    print(f"Scalar multiply: {v1} * 2 = {v4}")
    print(f"Vector comparison: {v1} < {v2} = {v1 < v2}")
    
    print("\n🧪 8. Context Manager Tests")
    result = multiple_context_managers()
    print(f"Context result: {result}")
    
    print("\n🧪 9. Container Protocol Tests")
    container = SmartContainer([1, 2, 3, 4, 5])
    print(f"Container length: {len(container)}")
    print(f"Container slice [1:3]: {list(container[1:3])}")
    print(f"Contains 3: {3 in container}")
    
    container.append(6)
    print(f"After append: {list(container)}")
    
    print("\n🧪 10. Generator Expression in Complex Context")
    # Generator expression with complex logic
    complex_gen = (x*x for x in range(10) if x % 2 == 0)
    filtered_squared = list(complex_gen)
    print(f"Complex generator: {filtered_squared}")
    
    # Nested generator expressions
    nested_gen = ((x, y) for x in range(3) for y in range(3) if x != y)
    pairs = list(nested_gen)
    print(f"Nested generator: {pairs}")

# Main execution
if __name__ == "__main__":
    try:
        test_advanced_features()
        print("\n" + "="*60)
        print("🎉 ALL PYTHON 3.12 ADVANCED FEATURES TESTED SUCCESSFULLY!")
        print("="*60)
    except Exception as e:
        print(f"\n💥 Test failed: {e}")
        import traceback
        traceback.print_exc()