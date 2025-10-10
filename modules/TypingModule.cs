using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy.Modules
{
    /// <summary>
    /// Python typing 모듈 구현 - 타입 힌트 및 제네릭 지원
    /// </summary>
    public static class TypingModule
    {
        public static PyModule CreateTypingModule()
        {
            var module = new PyModule("typing", "C:\\Users\\m11\\Desktop\\work\\sharpPy\\modules\\typing.py");

            // Union type support
            module.ModuleDict["Union"] = new PyUnionType();
            module.ModuleDict["Optional"] = new PyOptionalType();

            // Generic types
            module.ModuleDict["List"] = new PyGenericAlias("List");
            module.ModuleDict["Dict"] = new PyGenericAlias("Dict");
            module.ModuleDict["Set"] = new PyGenericAlias("Set");
            module.ModuleDict["Tuple"] = new PyGenericAlias("Tuple");

            // Type variables
            module.ModuleDict["TypeVar"] = new PyTypeVarFactory();
            module.ModuleDict["Generic"] = new PyGenericType();

            // Callable
            module.ModuleDict["Callable"] = new PyCallableType();

            // Any and special forms
            module.ModuleDict["Any"] = new PyAnyType();
            module.ModuleDict["NoReturn"] = new PyNoReturnType();

            // Protocol support (basic)
            module.ModuleDict["Protocol"] = new PyProtocolType();

            return module;
        }
    }

    /// <summary>
    /// Union type implementation for typing.Union
    /// </summary>
    public class PyUnionType : PyObject
    {
        public override PyType GetPyType() => PyType.TypeType;

        public override PyObject GetItem(PyObject key)
        {
            if (key is PyTuple tuple)
            {
                return new PyUnionInstance(tuple.Items.ToArray());
            }
            else
            {
                return new PyUnionInstance(new PyObject[] { key });
            }
        }

        public override string ToString() => "typing.Union";

        public static PyUnionType Create() => new PyUnionType();
    }

    /// <summary>
    /// Union type instance (e.g., Union[int, str])
    /// </summary>
    public class PyUnionInstance : PyObject
    {
        public PyObject[] Types { get; }

        public PyUnionInstance(PyObject[] types)
        {
            Types = types ?? throw new ArgumentNullException(nameof(types));
        }

        public override PyType GetPyType() => PyType.GenericAliasType;

        public override string ToString()
        {
            var typeNames = Types.Select(t => t.ToString()).ToArray();
            return $"typing.Union[{string.Join(", ", typeNames)}]";
        }

        /// <summary>
        /// Check if a value matches this union type
        /// </summary>
        public bool IsInstance(PyObject value)
        {
            foreach (var type in Types)
            {
                if (type is PyBuiltinType builtinType)
                {
                    if (value.GetPyType().Name == builtinType.Name)
                        return true;
                }
                else if (type.AsString() == value.GetPyType().Name)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Optional type (Union[T, None])
    /// </summary>
    public class PyOptionalType : PyObject
    {
        public override PyType GetPyType() => PyType.TypeType;

        public override PyObject GetItem(PyObject key)
        {
            return new PyUnionInstance(new PyObject[] { key, PyNone.Instance });
        }

        public override string ToString() => "typing.Optional";
    }

    /// <summary>
    /// Generic alias for List[T], Dict[K, V], etc.
    /// </summary>
    public class PyGenericAlias : PyObject
    {
        public string Name { get; }

        public PyGenericAlias(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public override PyType GetPyType() => PyType.TypeType;

        public override PyObject GetItem(PyObject key)
        {
            return new PyGenericAliasInstance(Name, key);
        }

        public override string ToString() => $"typing.{Name}";
    }

    /// <summary>
    /// Generic alias instance (e.g., List[int], Dict[str, int])
    /// </summary>
    public class PyGenericAliasInstance : PyObject
    {
        public string GenericName { get; }
        public PyObject TypeArg { get; }

        public PyGenericAliasInstance(string genericName, PyObject typeArg)
        {
            GenericName = genericName ?? throw new ArgumentNullException(nameof(genericName));
            TypeArg = typeArg ?? throw new ArgumentNullException(nameof(typeArg));
        }

        public override PyType GetPyType() => PyType.GenericAliasType;

        public override string ToString()
        {
            if (TypeArg is PyTuple tuple)
            {
                var args = tuple.Items.Select(item => item.ToString()).ToArray();
                return $"typing.{GenericName}[{string.Join(", ", args)}]";
            }
            else
            {
                return $"typing.{GenericName}[{TypeArg}]";
            }
        }
    }

    /// <summary>
    /// TypeVar factory for creating type variables
    /// </summary>
    public class PyTypeVarFactory : PyBuiltinFunction
    {
        public PyTypeVarFactory() : base("TypeVar", CreateTypeVar)
        {
        }

        private static PyObject CreateTypeVar(PyObject[] args, PyDict? kwargs)
        {
            if (args.Length < 1)
                throw PyTypeError.Create("TypeVar() missing 1 required positional argument: 'name'");

            var name = args[0].AsString();
            var constraints = args.Skip(1).ToArray();

            return new PyTypeVar(name, constraints);
        }
    }

    /// <summary>
    /// Type variable implementation
    /// </summary>
    public class PyTypeVar : PyObject
    {
        public string Name { get; }
        public PyObject[] Constraints { get; }

        public PyTypeVar(string name, PyObject[] constraints)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Constraints = constraints ?? new PyObject[0];
        }

        public override PyType GetPyType() => PyType.TypeVarType;

        public override string ToString()
        {
            if (Constraints.Length > 0)
            {
                var constraintNames = Constraints.Select(c => c.ToString()).ToArray();
                return $"~{Name} (bound by {string.Join(", ", constraintNames)})";
            }
            return $"~{Name}";
        }
    }

    /// <summary>
    /// Generic base class
    /// </summary>
    public class PyGenericType : PyObject
    {
        public override PyType GetPyType() => PyType.TypeType;

        public override PyObject GetItem(PyObject key)
        {
            return new PyGenericInstance(key);
        }

        public override string ToString() => "typing.Generic";
    }

    /// <summary>
    /// Generic instance (Generic[T])
    /// </summary>
    public class PyGenericInstance : PyObject
    {
        public PyObject TypeParam { get; }

        public PyGenericInstance(PyObject typeParam)
        {
            TypeParam = typeParam ?? throw new ArgumentNullException(nameof(typeParam));
        }

        public override PyType GetPyType() => PyType.GenericAliasType;

        public override string ToString() => $"typing.Generic[{TypeParam}]";
    }

    /// <summary>
    /// Callable type support
    /// </summary>
    public class PyCallableType : PyObject
    {
        public override PyType GetPyType() => PyType.TypeType;

        public override PyObject GetItem(PyObject key)
        {
            return new PyCallableInstance(key);
        }

        public override string ToString() => "typing.Callable";
    }

    /// <summary>
    /// Callable instance (Callable[[int, str], bool])
    /// </summary>
    public class PyCallableInstance : PyObject
    {
        public PyObject TypeArg { get; }

        public PyCallableInstance(PyObject typeArg)
        {
            TypeArg = typeArg ?? throw new ArgumentNullException(nameof(typeArg));
        }

        public override PyType GetPyType() => PyType.GenericAliasType;

        public override string ToString() => $"typing.Callable[{TypeArg}]";
    }

    /// <summary>
    /// Any type (matches anything)
    /// </summary>
    public class PyAnyType : PyObject
    {
        public override PyType GetPyType() => PyType.TypeType;
        public override string ToString() => "typing.Any";
    }

    /// <summary>
    /// NoReturn type (for functions that never return)
    /// </summary>
    public class PyNoReturnType : PyObject
    {
        public override PyType GetPyType() => PyType.TypeType;
        public override string ToString() => "typing.NoReturn";
    }

    /// <summary>
    /// Protocol type (basic implementation)
    /// </summary>
    public class PyProtocolType : PyObject
    {
        public override PyType GetPyType() => PyType.TypeType;
        public override string ToString() => "typing.Protocol";
    }
}