namespace SharpPy
{

    #region Core Object System

    // Python의 object 역할 - 모든 객체의 베이스
    public abstract class PyObject
    {
        public virtual PyType GetPyType() => PyType.ObjectType;
        public virtual string GetTypeName() => this.GetType().Name;

        // Python의 attribute access protocol
        public virtual PyObject GetAttribute(string name)
        {
            return PyGetAttribute(name);
        }

        public virtual void SetAttribute(string name, PyObject value)
        {
            PySetAttribute(name, value);
        }

        public virtual void DelAttribute(string name)
        {
            PyDelAttribute(name);
        }

        // 내부 attribute 처리 메서드들 (MRO 기반)
        protected virtual PyObject PyGetAttribute(string name)
        {
            var type = GetPyType();

            // 1. 타입의 MRO에서 descriptor 찾기
            IDescriptor descriptor = null;
            PyObject attr = null;

            foreach (var mroType in type.MRO)
            {
                if (mroType is PyClass customType && customType.ClassDict.ContainsKey(name))
                {
                    attr = customType.ClassDict[name];
                    if (attr is IDescriptor desc)
                    {
                        descriptor = desc;
                    }
                    break;
                }
            }

            // 2. data descriptor라면 우선권을 가짐
            if (descriptor != null && descriptor.IsDataDescriptor())
            {
                return descriptor.Get(this, type);
            }

            // 3. instance dictionary 확인
            if (this is PyClassInstance instance)
            {
                if (instance.InstanceDict.TryGetValue(name, out PyObject value))
                {
                    return value;
                }
            }

            // 4. non-data descriptor 또는 일반 attribute
            if (descriptor != null)
            {
                return descriptor.Get(this, type);
            }

            if (attr != null)
            {
                // 일반 함수는 method로 바인딩
                if (attr is PyFunction func && this is PyClassInstance)
                {
                    return new PyMethod(this, func);
                }
                return attr;
            }

            // 5. __getattr__ 호출 (있다면)
            if (HasCustomGetAttr())
            {
                return CallGetAttr(name);
            }

            throw new AttributeError($"'{GetTypeName()}' object has no attribute '{name}'");
        }

        protected virtual void PySetAttribute(string name, PyObject value)
        {
            var type = GetPyType();

            // 1. 타입의 MRO에서 data descriptor 찾기
            foreach (var mroType in type.MRO)
            {
                if (mroType is PyClass customType && customType.ClassDict.ContainsKey(name))
                {
                    var attr = customType.ClassDict[name];
                    if (attr is IDescriptor desc && desc.IsDataDescriptor())
                    {
                        desc.Set(this, value);
                        return;
                    }
                    break;
                }
            }

            // 2. instance dictionary에 저장
            if (this is PyClassInstance instance)
            {
                instance.InstanceDict[name] = value;
            }
            else if (this is PyClass customType)
            {
                customType.ClassDict[name] = value;
            }
            else
            {
                throw new AttributeError($"'{GetTypeName()}' object attribute '{name}' is read-only");
            }
        }

        protected virtual void PyDelAttribute(string name)
        {
            if (this is PyClassInstance instance)
            {
                if (instance.InstanceDict.ContainsKey(name))
                {
                    instance.InstanceDict.Remove(name);
                    return;
                }
            }
            throw new AttributeError($"'{GetTypeName()}' object has no attribute '{name}'");
        }

        protected virtual bool HasCustomGetAttr() => false;
        protected virtual PyObject CallGetAttr(string name) => null;

        // Python의 __call__ 메서드 - 모든 객체가 잠재적으로 호출 가능
        public virtual PyObject Call(params PyObject[] args)
        {
            // __call__ attribute가 있는지 확인
            try
            {
                var callMethod = GetAttribute("__call__");
                if (callMethod is PyMethod method)
                {
                    return method.Call(args);
                }
                else if (callMethod is PyFunction func)
                {
                    // __call__이 unbound function인 경우 self를 첫 번째 인자로 추가
                    var newArgs = new PyObject[args.Length + 1];
                    newArgs[0] = this;
                    Array.Copy(args, 0, newArgs, 1, args.Length);
                    return func.Call(newArgs);
                }
            }
            catch (AttributeError)
            {
                // __call__ attribute가 없음
            }

            throw new TypeError($"'{GetTypeName()}' object is not callable");
        }

        // callable() 내장 함수를 위한 메서드
        public virtual bool IsCallable()
        {
            // 기본적으로 __call__ attribute가 있으면 호출 가능
            try
            {
                GetAttribute("__call__");
                return true;
            }
            catch (AttributeError)
            {
                return false;
            }
        }
    }

    #endregion
}