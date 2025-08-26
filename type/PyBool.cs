namespace SharpPy
{
    public class PyBool : PyObject
    {
        public bool Value { get; }
        public static readonly PyBool True = new PyBool(true);
        public static readonly PyBool False = new PyBool(false);

        private PyBool(bool value) => Value = value;

        public static PyBool FromBool(bool value) => value ? True : False;

        public override string GetTypeName() => "bool";
        public override string ToString() => Value ? "True" : "False";
    }
}