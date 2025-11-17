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
        protected readonly PyObject? _getter;
        protected readonly PyObject? _setter;
        protected readonly PyObject? _deleter;
        protected readonly PyObject? _doc;

        public PyProperty(PyObject? getter = null, PyObject? setter = null, PyObject? deleter = null, PyObject? doc = null)
        {
            _getter = getter;
            _setter = setter;
            _deleter = deleter;
            _doc = doc;

            // CPython 호환: property type descriptor 초기화
            InitializePropertyDescriptors();
        }

        /// <summary>
        /// CPython 호환: property 타입의 descriptor 테이블 초기화
        /// </summary>
        private static void InitializePropertyDescriptors()
        {
            var propType = PyType.PropertyType;

            // setter 메서드 descriptor
            propType.TypeDict["setter"] = new PyMethodDescriptor(
                "setter",
                propType,
                (self, args, kwargs) => {
                    if (self is not PyProperty prop)
                        throw PyTypeError.Create("descriptor 'setter' for 'property' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 PyProperty.setter called with {args.Length} args");
                    #endif
                    if (args.Length != 1)
                        throw PyTypeError.Create($"setter() takes exactly one argument ({args.Length} given)");
                    if (!args[0].IsCallable())
                        throw PyTypeError.Create("setter() argument must be callable");

                    #if DEBUG_LOG
                    Console.WriteLine($"   → Creating new PyProperty with setter: {args[0].GetTypeName()}");
                    #endif
                    var newProperty = new PyProperty(prop._getter, args[0], prop._deleter, prop._doc);
                    #if DEBUG_LOG
                    Console.WriteLine($"   → New property: getter={newProperty._getter?.GetTypeName()}, setter={newProperty._setter?.GetTypeName()}, deleter={newProperty._deleter?.GetTypeName()}");
                    #endif
                    return newProperty;
                },
                minArgs: 1,
                maxArgs: 1
            );

            // deleter 메서드 descriptor
            propType.TypeDict["deleter"] = new PyMethodDescriptor(
                "deleter",
                propType,
                (self, args, kwargs) => {
                    if (self is not PyProperty prop)
                        throw PyTypeError.Create("descriptor 'deleter' for 'property' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 PyProperty.deleter called with {args.Length} args");
                    #endif
                    if (args.Length != 1)
                        throw PyTypeError.Create($"deleter() takes exactly one argument ({args.Length} given)");
                    if (!args[0].IsCallable())
                        throw PyTypeError.Create("deleter() argument must be callable");

                    #if DEBUG_LOG
                    Console.WriteLine($"   → Creating new PyProperty with deleter: {args[0].GetTypeName()}");
                    #endif
                    var newProperty = new PyProperty(prop._getter, prop._setter, args[0], prop._doc);
                    #if DEBUG_LOG
                    Console.WriteLine($"   → New property: getter={newProperty._getter?.GetTypeName()}, setter={newProperty._setter?.GetTypeName()}, deleter={newProperty._deleter?.GetTypeName()}");
                    #endif
                    return newProperty;
                },
                minArgs: 1,
                maxArgs: 1
            );

            // getter 메서드 descriptor
            propType.TypeDict["getter"] = new PyMethodDescriptor(
                "getter",
                propType,
                (self, args, kwargs) => {
                    if (self is not PyProperty prop)
                        throw PyTypeError.Create("descriptor 'getter' for 'property' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    if (args.Length != 1)
                        throw PyTypeError.Create($"getter() takes exactly one argument ({args.Length} given)");
                    if (!args[0].IsCallable())
                        throw PyTypeError.Create("getter() argument must be callable");

                    return new PyProperty(args[0], prop._setter, prop._deleter, prop._doc);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // fget getset descriptor
            propType.TypeDict["fget"] = new PyGetSetDescriptor(
                "fget",
                propType,
                getter: self => {
                    if (self is PyProperty prop)
                        return prop._getter ?? (PyObject)PyNone.Instance;
                    throw PyTypeError.Create("descriptor 'fget' for 'property' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                }
            );

            // fset getset descriptor
            propType.TypeDict["fset"] = new PyGetSetDescriptor(
                "fset",
                propType,
                getter: self => {
                    if (self is PyProperty prop)
                        return prop._setter ?? (PyObject)PyNone.Instance;
                    throw PyTypeError.Create("descriptor 'fset' for 'property' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                }
            );

            // fdel getset descriptor
            propType.TypeDict["fdel"] = new PyGetSetDescriptor(
                "fdel",
                propType,
                getter: self => {
                    if (self is PyProperty prop)
                        return prop._deleter ?? (PyObject)PyNone.Instance;
                    throw PyTypeError.Create("descriptor 'fdel' for 'property' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                }
            );

            // CPython 3.12 호환: Descriptor protocol methods
            // Objects/descrobject.c:1979-1980 property type definition
            // tp_descr_get = property_descr_get
            // tp_descr_set = property_descr_set
            // Python code sees these as __get__, __set__, __delete__ methods

            // __get__ method descriptor - CPython: property_descr_get
            propType.TypeDict["__get__"] = new PyMethodDescriptor(
                "__get__",
                propType,
                (self, args, kwargs) => {
                    if (self is not PyProperty prop)
                        throw PyTypeError.Create("descriptor '__get__' for 'property' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    // __get__(self, instance, owner=None)
                    if (args.Length < 1 || args.Length > 2)
                        throw PyTypeError.Create($"__get__() takes 2 or 3 positional arguments but {args.Length + 1} were given");

                    PyObject instance = args[0];
                    PyType owner = args.Length > 1 && args[1] is PyType ownerType ? ownerType : null;

                    // CPython behavior: if instance is None, return the property object itself
                    if (instance is PyNone)
                        return prop;

                    return prop.Get(instance, owner);
                },
                minArgs: 1,
                maxArgs: 2
            );

            // __set__ method descriptor - CPython: property_descr_set (for setting)
            propType.TypeDict["__set__"] = new PyMethodDescriptor(
                "__set__",
                propType,
                (self, args, kwargs) => {
                    if (self is not PyProperty prop)
                        throw PyTypeError.Create("descriptor '__set__' for 'property' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    // __set__(self, instance, value)
                    if (args.Length != 2)
                        throw PyTypeError.Create($"__set__() takes exactly 2 arguments ({args.Length} given)");

                    PyObject instance = args[0];
                    PyObject value = args[1];

                    prop.Set(instance, value);
                    return PyNone.Instance;
                },
                minArgs: 2,
                maxArgs: 2
            );

            // __delete__ method descriptor - CPython: property_descr_set (for deleting)
            propType.TypeDict["__delete__"] = new PyMethodDescriptor(
                "__delete__",
                propType,
                (self, args, kwargs) => {
                    if (self is not PyProperty prop)
                        throw PyTypeError.Create("descriptor '__delete__' for 'property' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    // __delete__(self, instance)
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__delete__() takes exactly 1 argument ({args.Length} given)");

                    PyObject instance = args[0];

                    prop.Delete(instance);
                    return PyNone.Instance;
                },
                minArgs: 1,
                maxArgs: 1
            );
        }

        public PyObject Get(PyObject instance, PyType owner)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PyProperty.Get called: instance={instance?.GetType().Name}, owner={owner?.Name}");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"   → getter={_getter?.GetTypeName()}, setter={_setter?.GetTypeName()}, deleter={_deleter?.GetTypeName()}");
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

            if (!_getter.IsCallable())
            {
                throw PyTypeError.Create($"property getter is not callable");
            }

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
            Console.WriteLine($"   → setter={_setter?.GetTypeName()}");
            #endif

            if (_setter == null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   ❌ No setter available - throwing AttributeError");
                #endif
                throw PyAttributeError.Create("can't set attribute");
            }

            if (!_setter.IsCallable())
            {
                throw PyTypeError.Create($"property setter is not callable");
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
            Console.WriteLine($"   → deleter={_deleter?.GetTypeName()}");
            #endif

            if (_deleter == null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   ❌ No deleter available - throwing AttributeError");
                #endif
                throw PyAttributeError.Create("can't delete attribute");
            }

            if (!_deleter.IsCallable())
            {
                throw PyTypeError.Create($"property deleter is not callable");
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

        // CPython 3.12 호환: property는 항상 data descriptor임 (tp_descr_set이 항상 존재)
        // property_descr_set 함수는 setter가 없을 때도 존재하며, 호출 시 AttributeError 발생
        // Reference: Objects/descrobject.c:1630 property_descr_set, line 1980 tp_descr_set
        public bool IsDataDescriptor() => true;

        // Property decorator chaining methods - CPython 호환: descriptor 테이블 사용
        public override PyObject GetAttribute(string name)
        {
            // CPython 3.12 호환: GenericGetAttribute를 통해 descriptor 테이블 조회
            return GenericGetAttribute(name);
        }

        public override string GetTypeName() => "property";
        public override PyType GetPyType() => PyType.PropertyType;
        public override string ToString() => "<property object>";
    }

    #endregion
}