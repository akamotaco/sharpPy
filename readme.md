# SharpPy (Coding with AI)
Python interpreter (not native python) by pure C# code.\
It working Godot on Android.\
(*.py resources exporting required when Godot Export)

## Basic test (dotnet core)
 > dotnet run

## Script test (dotnet core)
 > dotnet run test\test.py

## with Godot
SharpPy working with Godot Engine.
(remove main.cs, sharppy.csproj, sharppy.sln files)

# Godot implementation

## Create interpreter
  > _Py = new SharpPy.PythonInterpreter();

## Declare built-in function
```c#
var env = _Py.GetGlobalEnv();
var module = env.GetVariable("__builtins__") as PythonModule;

module.SetAttribute("Log", new BuiltinFunction("Log", args =>
{
    GD.Print("Python:" + args[0].AsString());
    return PythonNone.Instance;
}));
```

### complex function 1
```c#
module.SetAttribute("_func1", new BuiltinFunction("_func1", py_args =>
{
    // _func1(arg1:str, arg2:int, arg3:bool) -> int:
    if(py_args.Count != 3)
        throw new PythonException("TypeError", "UpdateTextWindow() takes exact 3 arguments");

    var args1 = py_args[0].AsString();
    var args2 = py_args[1].AsInt();
    var args3 = py_args[2].AsBool();
    ...
    return new PythonInt(2 * args2);
}
```

### complex function 2
```c#
module.SetAttribute("_func2", new BuiltinFunction("_func2", py_args =>
{
    // _func2(arg1:str, arg2:list[str]) -> None:
    if (py_args.Count != 2)
        throw new PythonException("TypeError", "RemoveChildAsset() takes exact 2 arguments");

    var args1 = py_args[0].AsString();
    var args2 = py_args[1] is PythonList pl ? PythonInterop.ConvertList(pl).OfType<string>().ToArray() : null;
    ...
    return PythonNone.Instance;
}
```

### complex function 3 (with Godot Method)
```c#
module.SetAttribute("_func3", new BuiltinFunction("_func3", py_args =>
{
    // _func3(arg1:str, arg2:str, arg3:list[Godot.Variant]) -> None:
    if (py_args.Count < 3)
        throw new PythonException("TypeError", "CallAssetMethod() takes at least 3 arguments");

    var arg1 = py_args[0].AsString();
    var arg2 = py_args[1].AsString();
    var arg3 = PythonInterop.ConvertToVariantArray(py_args.Skip(2).ToList());

    ...
    GodotObject.Call(method_name, arg3);
    ...

    return PythonNone.Instance;
}
```



## Call python function
```c#
_Py.Execute($"py_func('{arg1}', '{arg2}')", "call_from_godot");
```

### with try-except

```c#
try
{
    _Py.Execute($"py_func('{arg1}', '{arg2}')", "call_from_godot");
}
catch (PythonException ex)
{
    GD.PrintErr($"=== Python Error ===");
    GD.PrintErr($"{ex.Type}: {ex.Message}");
    if (ex.Line > 0)
        GD.PrintErr($"Line {ex.Line}, Column {ex.Column}");
    GD.PrintErr($"===================");
}
catch (Exception ex)
{
    var innerEx = ex;
    while (innerEx != null)
    {
        if (innerEx is PythonException pyEx)
        {
            GD.PrintErr($"Python Error (wrapped): {pyEx.Type}: {pyEx.Message}");
            return;
        }
        innerEx = innerEx.InnerException;
    }
    GD.PrintErr($"Unexpected error: {ex.Message}");
}
```