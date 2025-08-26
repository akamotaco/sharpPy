namespace SharpPy
{
    public class PyNone : PyObject
    {
        public static readonly PyNone Instance = new PyNone();
        private PyNone() { }
        public override string GetTypeName() => "NoneType";
        public override string ToString() => "None";
    }
}