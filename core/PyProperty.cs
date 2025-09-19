namespace SharpPy
{
    #region Descriptor Protocol

// Descriptor 프로토콜 인터페이스
public interface IDescriptor
{
    PyObject Get(PyObject instance, PyType owner);
    void Set(PyObject instance, PyObject value);
    void Delete(PyObject instance);
    bool IsDataDescriptor(); // __set__ 또는 __delete__가 있으면 data descriptor
}

    // Python의 property 구현
    public class PyProperty : PyObject, IDescriptor
    {
        protected readonly PyFunction _getter;
        protected readonly PyFunction _setter;
        protected readonly PyFunction _deleter;

        public PyProperty(PyFunction getter, PyFunction setter = null, PyFunction deleter = null)
        {
            _getter = getter;
            _setter = setter;
            _deleter = deleter;
        }

        public PyObject Get(PyObject instance, PyType owner)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PyProperty.Get called: instance={instance?.GetType().Name}, owner={owner?.Name}");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"   → getter={_getter?.Name}, setter={_setter?.Name}, deleter={_deleter?.Name}");
            #endif

            if (_getter == null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   ❌ No getter available - throwing AttributeError");
                #endif
                throw PyAttributeError.Create("unreadable attribute");
            }

            if (instance == null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → Returning property object itself (class-level access)");
                #endif
                return this; // 클래스에서 접근할 때는 property 객체 자체 반환
            }

            #if DEBUG_LOG
            Console.WriteLine($"   → Calling getter with instance: {instance.GetType().Name}");
            #endif
            try
            {
                var result = _getter.Call(new PyObject[] { instance }, null);
                #if DEBUG_LOG
                Console.WriteLine($"   ✅ Getter returned: {result?.ToString()}");
                #endif
                return result;
            }
            catch (Exception ex)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   ❌ Getter failed: {ex.Message}");
                #endif
                throw;
            }
        }

        public void Set(PyObject instance, PyObject value)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PyProperty.Set called: instance={instance?.GetType().Name}, value={value}");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"   → setter={_setter?.Name}");
            #endif

            if (_setter == null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   ❌ No setter available - throwing AttributeError");
                #endif
                throw PyAttributeError.Create("can't set attribute");
            }

            #if DEBUG_LOG
            Console.WriteLine($"   → Calling setter with value: {value}");
            #endif
            try
            {
                _setter.Call(new PyObject[] { instance, value }, null);
                #if DEBUG_LOG
                Console.WriteLine($"   ✅ Setter completed successfully");
                #endif
            }
            catch (Exception ex)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   ❌ Setter failed: {ex.Message}");
                #endif
                throw;
            }
        }

        public void Delete(PyObject instance)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PyProperty.Delete called: instance={instance?.GetType().Name}");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"   → deleter={_deleter?.Name}");
            #endif

            if (_deleter == null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   ❌ No deleter available - throwing AttributeError");
                #endif
                throw PyAttributeError.Create("can't delete attribute");
            }

            #if DEBUG_LOG
            Console.WriteLine($"   → Calling deleter");
            #endif
            try
            {
                _deleter.Call(new PyObject[] { instance }, null);
                #if DEBUG_LOG
                Console.WriteLine($"   ✅ Deleter completed successfully");
                #endif
            }
            catch (Exception ex)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   ❌ Deleter failed: {ex.Message}");
                #endif
                throw;
            }
        }

        public bool IsDataDescriptor() => _setter != null || _deleter != null;

        // Property decorator chaining methods
        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "setter":
                    return new PyBuiltinFunction("setter", args => {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 PyProperty.setter called with {args.Length} args");
                        #endif
                        if (args.Length != 1) throw PyTypeError.Create($"setter() takes exactly one argument ({args.Length} given)");
                        if (args[0] is not PyFunction func) throw PyTypeError.Create("setter() argument must be a function");
                        #if DEBUG_LOG
                        Console.WriteLine($"   → Creating new PyProperty with setter: {func.Name}");
                        #endif
                        var newProperty = new PyProperty(_getter, func, _deleter);
                        #if DEBUG_LOG
                        Console.WriteLine($"   → New property: getter={newProperty._getter?.Name}, setter={newProperty._setter?.Name}, deleter={newProperty._deleter?.Name}");
                        #endif
                        return newProperty;
                    });
                case "deleter":
                    return new PyBuiltinFunction("deleter", args => {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 PyProperty.deleter called with {args.Length} args");
                        #endif
                        if (args.Length != 1) throw PyTypeError.Create($"deleter() takes exactly one argument ({args.Length} given)");
                        if (args[0] is not PyFunction func) throw PyTypeError.Create("deleter() argument must be a function");
                        #if DEBUG_LOG
                        Console.WriteLine($"   → Creating new PyProperty with deleter: {func.Name}");
                        #endif
                        var newProperty = new PyProperty(_getter, _setter, func);
                        #if DEBUG_LOG
                        Console.WriteLine($"   → New property: getter={newProperty._getter?.Name}, setter={newProperty._setter?.Name}, deleter={newProperty._deleter?.Name}");
                        #endif
                        return newProperty;
                    });
                case "getter":
                    return new PyBuiltinFunction("getter", args => {
                        if (args.Length != 1) throw PyTypeError.Create($"getter() takes exactly one argument ({args.Length} given)");
                        if (args[0] is not PyFunction func) throw PyTypeError.Create("getter() argument must be a function");
                        return new PyProperty(func, _setter, _deleter);
                    });
                case "fget":
                    return _getter ?? (PyObject)PyNone.Instance;
                case "fset":
                    return _setter ?? (PyObject)PyNone.Instance;
                case "fdel":
                    return _deleter ?? (PyObject)PyNone.Instance;
                default:
                    return base.GetAttribute(name);
            }
        }

        public override string GetTypeName() => "property";
        public override string ToString() => "<property object>";
    }

    #endregion
}