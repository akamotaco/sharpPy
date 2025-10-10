using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 compatible implementation of the 'type' metaclass
    /// This is the base metaclass for all Python classes
    /// 
    /// Key characteristics:
    /// - type.__class__ == type (type is its own metaclass)
    /// - type.__bases__ == (object,)
    /// - type.__init__ exists and is callable from subclasses
    /// - type.__new__ creates new classes
    /// - type.__call__ instantiates classes
    /// </summary>
    public class PyTypeMetaclass : PyClass
    {
        private static PyTypeMetaclass _instance;
        
        /// <summary>
        /// The global 'type' object - equivalent to CPython's type
        /// </summary>
        public static PyTypeMetaclass Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = CreateTypeMetaclass();
                }
                return _instance;
            }
        }

        private PyTypeMetaclass(string name, PyType[] baseTypes, Dictionary<string, PyObject> classDict) 
            : base(name, baseTypes, classDict)
        {
            // type is its own metaclass
            this.Metaclass = this;
        }

        /// <summary>
        /// Creates the global type metaclass instance
        /// </summary>
        private static PyTypeMetaclass CreateTypeMetaclass()
        {
            #if DEBUG_LOG
            Console.WriteLine("🏗️ Creating global 'type' metaclass");
            #endif
            
            var classDict = new Dictionary<string, PyObject>();

            // type.__init__(cls, name, bases, namespace) 
            classDict["__init__"] = new PyBuiltinMethod("__init__", (self, args) => 
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔧 type.__init__ called with {args.Length} args");
                #endif
                
                // type.__init__ doesn't need to do much - just exist for super() calls
                // The real work is done in type.__new__
                return PyNone.Instance;
            }, 4); // self + 3 args

            // type.__new__(cls, name, bases, namespace)
            classDict["__new__"] = new PyBuiltinMethod("__new__", (self, args) => 
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔧 type.__new__ called with {args.Length} args");
                #endif
                
                if (args.Length == 1)
                {
                    // type(obj) - return type of object
                    return args[0].GetPyType();
                }
                else if (args.Length == 3)
                {
                    // Metaclass.__new__(cls, name, bases, namespace) called from super()
                    // 'self' is the metaclass, args are [name, bases, namespace]
                    var newArgs = new PyObject[] { self, args[0], args[1], args[2] };
                    return CreateNewClass(newArgs);
                }
                else if (args.Length == 4)
                {
                    // Direct type.__new__(cls, name, bases, namespace)
                    return CreateNewClass(args);
                }
                else
                {
                    throw PyTypeError.Create($"type.__new__() takes 1, 3 or 4 arguments ({args.Length} given)");
                }
            }, -1); // variable args

            // type.__call__(cls, *args, **kwargs) - class instantiation
            classDict["__call__"] = new PyBuiltinMethod("__call__", (self, args) => 
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔧 type.__call__ called: instantiating {self}");
                #endif
                
                if (self is PyClass pyClass)
                {
                    // This is class instantiation: MyClass() -> instance
                    return pyClass.CreateInstance(args);
                }
                
                throw PyTypeError.Create($"'{self}' object is not callable");
            }, -1); // variable args

            // type.__str__()
            classDict["__str__"] = new PyBuiltinMethod("__str__", (self, args) => 
            {
                if (self is PyClass pyClass)
                {
                    return new PyString($"<class '{pyClass.Name}'>");
                }
                return new PyString($"<type '{self}'>"); 
            }, 1);

            // type.__repr__() - same as __str__ for type
            classDict["__repr__"] = classDict["__str__"];

            // Add other essential type methods...
            classDict["mro"] = new PyBuiltinMethod("mro", (self, args) => 
            {
                if (self is PyClass pyClass)
                {
                    return new PyList(pyClass.MRO.Cast<PyObject>().ToList());
                }
                return new PyList();
            }, 1);

            // type inherits from object
            var baseTypes = new PyType[] { PyType.ObjectType };

            var typeClass = new PyTypeMetaclass("type", baseTypes, classDict);
            
            #if DEBUG_LOG
            Console.WriteLine("✅ Global 'type' metaclass created successfully");
            #endif
            return typeClass;
        }

        /// <summary>
        /// Implementation of type.__new__ for creating new classes
        /// </summary>
        private static PyObject CreateNewClass(PyObject[] args)
        {
            var cls = args[0];        // metaclass (should be type or subclass)
            var name = args[1];       // class name
            var bases = args[2];      // base classes tuple
            var namespaceDict = args[3];  // class namespace dict

            #if DEBUG_LOG
            Console.WriteLine($"🏗️ type.__new__ creating class: {name}");
            #endif

            // Convert arguments
            if (!(name is PyString nameStr))
                throw PyTypeError.Create("type.__new__() name must be string");
                
            if (!(bases is PyTuple basesTuple))
                throw PyTypeError.Create("type.__new__() bases must be tuple");
                
            if (!(namespaceDict is PyDict pyDict))
                throw PyTypeError.Create("type.__new__() namespace must be dict");

            // Convert bases tuple to PyType array
            var baseTypes = basesTuple.Items.Cast<PyType>().ToArray();
            
            // Convert namespace dict to class dict
            var classDict = new Dictionary<string, PyObject>();
            var dictItems = pyDict.Items();
            foreach (var item in dictItems.Items)
            {
                if (item is PyTuple tuple && tuple.Items.Length == 2)
                {
                    if (tuple.Items[0] is PyString keyStr)
                    {
                        classDict[keyStr.Value] = tuple.Items[1];
                    }
                }
            }

            // Create the new class
            var newClass = new PyClass(nameStr.Value, baseTypes, classDict);
            
            // Set metaclass if specified
            if (cls is PyClass metaclass && metaclass != Instance)
            {
                newClass.Metaclass = metaclass;
            }
            else
            {
                newClass.Metaclass = Instance; // default to type
            }

            #if DEBUG_LOG
            Console.WriteLine($"✅ Created class {nameStr.Value} with metaclass {newClass.Metaclass?.Name}");
            #endif
            return newClass;
        }

        /// <summary>
        /// Override GetPyType to return self (type is its own type)
        /// </summary>
        public override PyType GetPyType()
        {
            return this; // type.__class__ == type
        }

        /// <summary>
        /// Override Call to handle type() calls properly
        /// </summary>
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PyTypeMetaclass.Call called with {args.Length} args");
            #endif
            for (int i = 0; i < args.Length; i++)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   arg[{i}]: {args[i]?.GetType().Name} = {args[i]}");
                #endif
            }
            
            if (args.Length == 1)
            {
                // type(obj) - return type of object  
                return args[0].GetPyType();
            }
            else if (args.Length == 3)
            {
                // type(name, bases, namespace) - create new class
                // The metaclass itself is the first argument in CreateNewClass
                var newArgs = new PyObject[] { this, args[0], args[1], args[2] };
                return CreateNewClass(newArgs);
            }
            else if (args.Length == 4)
            {
                // This might be the case where it's called by __build_class__
                // args[0] might be the metaclass, args[1] name, args[2] bases, args[3] namespace
                return CreateNewClass(args);
            }
            else
            {
                throw PyTypeError.Create($"type() takes 1, 3 or 4 arguments ({args.Length} given)");
            }
        }
    }

    /// <summary>
    /// Helper class for built-in methods
    /// </summary>
    /// <summary>
    /// CPython 3.12 호환: Descriptor protocol을 구현한 builtin method
    /// 클래스에서 접근 시 unbound, 인스턴스에서 접근 시 자동으로 bound method 반환
    /// </summary>
    public class PyBuiltinMethod : PyObject, IDescriptor
    {
        public string Name { get; }
        public Func<PyObject, PyObject[], PyObject> Method { get; }
        public int ArgCount { get; } // -1 for variable args

        public PyBuiltinMethod(string name, Func<PyObject, PyObject[], PyObject> method, int argCount = -1)
        {
            Name = name;
            Method = method;
            ArgCount = argCount;
        }

        public override string ToString()
        {
            return $"<built-in method '{Name}'>";
        }

        public override PyType GetPyType()
        {
            return PyType.MethodType; // builtin methods are instances of method type
        }

        // Descriptor protocol: instance.method → bound method
        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null || instance == PyNone.Instance)
            {
                // 클래스에서 접근: Class.method → unbound
                return this;
            }

            // 인스턴스에서 접근: instance.method → bound method
            return new PyBoundBuiltinMethod(instance, this);
        }

        public void Set(PyObject instance, PyObject value)
        {
            throw PyAttributeError.Create($"can't set attribute '{Name}'");
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create($"can't delete attribute '{Name}'");
        }

        public bool IsDataDescriptor() => false; // non-data descriptor

        // Unbound call: 첫 인자를 self로 사용
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            // The first argument is 'self' for unbound methods
            if (args.Length < 1)
            {
                throw PyTypeError.Create($"{Name}() missing required 'self' argument");
            }

            var self = args[0];
            var methodArgs = new PyObject[args.Length - 1];
            Array.Copy(args, 1, methodArgs, 0, methodArgs.Length);

            return Method(self, methodArgs);
        }

        public override bool IsCallable()
        {
            return true;
        }
    }

    /// <summary>
    /// Bound builtin method: self가 이미 바인딩된 메서드
    /// </summary>
    public class PyBoundBuiltinMethod : PyObject
    {
        private PyObject _instance;
        private PyBuiltinMethod _method;

        public PyBoundBuiltinMethod(PyObject instance, PyBuiltinMethod method)
        {
            _instance = instance;
            _method = method;
        }

        public override string ToString()
        {
            return $"<bound method '{_method.Name}' of {_instance}>";
        }

        public override PyType GetPyType()
        {
            return PyType.MethodType;
        }

        // Bound call: self 자동 추가
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            return _method.Method(_instance, args);
        }

        public override bool IsCallable()
        {
            return true;
        }

        // Bound method attributes
        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "__self__" => _instance,
                "__func__" => _method,
                "__name__" => new PyString(_method.Name),
                _ => base.GetAttribute(name)
            };
        }
    }
}