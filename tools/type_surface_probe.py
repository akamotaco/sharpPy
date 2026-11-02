# The slots and methods of the builtin types: what vars(type) holds, as _check_methods and
# inspect read it. CPython's own tables are the reference.
import types

kinds = [
    object, type, int, bool, float, complex, str, bytes, bytearray, memoryview, list, tuple, dict, set, frozenset,
    range, slice, property, staticmethod, classmethod, super, enumerate, map, filter, zip, reversed,
    BaseException, Exception, StopIteration, OSError, SyntaxError, KeyError,
    types.FunctionType, types.MethodType, types.ModuleType, types.GeneratorType, types.CoroutineType,
    types.AsyncGeneratorType, types.CodeType, types.FrameType, types.TracebackType, types.CellType,
    types.MappingProxyType, types.SimpleNamespace, types.GenericAlias, types.UnionType,
    types.BuiltinFunctionType, types.WrapperDescriptorType, types.MethodWrapperType, types.MethodDescriptorType,
    types.ClassMethodDescriptorType, types.GetSetDescriptorType, types.MemberDescriptorType,
    type(iter([])), type(iter(())), type(iter({})), type(iter("")), type(iter(b"")), type(iter(set())), type(iter(range(1))),
    type({}.keys()), type({}.values()), type({}.items()),
]
for kind in kinds:
    print(kind.__name__, sorted(k for k in vars(kind) if k not in ('__doc__', '__module__', '__text_signature__')))
