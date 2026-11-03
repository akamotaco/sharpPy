using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// A type made by PyStructSequence_NewType (Objects/structseq.c). The field table lists every
    /// field in storage order, with null for one that is only reached by index; the first Visible of
    /// them are the tuple, and the others are read by name.
    /// </summary>
    internal sealed class PyStructSequenceType : PyType
    {
        internal readonly string[] Fields;
        internal readonly string[] Members;
        internal readonly int Visible;
        internal override string FullName => Module == null ? Name : Module + "." + Name;

        internal PyStructSequenceType(string module, string name, string[] fields, int visible = -1)
            : base(name, new[] { TupleType }, module)
        {
            Fields = fields;
            Visible = visible < 0 ? fields.Length : visible;
            var members = new List<string>();
            var matchArgs = new List<PyObject>();
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i] == null)
                    continue;
                int index = i;
                members.Add(fields[i]);
                if (i < Visible)
                    matchArgs.Add(new PyString(fields[i]));
                TypeDict[fields[i]] = new PyMemberDescriptor(fields[i], this, self => ((PyStructSequence)self).AllFields[index]);
            }
            Members = members.ToArray();
            // initialize_structseq_dict
            if (module != null && module != "sys")
                TypeDict["__module__"] = new PyString(module);
            TypeDict["n_sequence_fields"] = new PyInt(Visible);
            TypeDict["n_fields"] = new PyInt(fields.Length);
            TypeDict["n_unnamed_fields"] = new PyInt(fields.Length - members.Count);
            TypeDict["__match_args__"] = new PyTuple(matchArgs.ToArray());
            TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", this, (self, args, kwargs) => self.ToRepr());
            TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", this, (self, args, kwargs) => ((PyStructSequence)self).Reduce());
            if (Layout.NoInstances)
                return;
            ConstructedByNewSlot = true;
            TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", this, (cls, args, kwargs) => New((PyStructSequenceType)cls, args, kwargs),
                acceptsKwargs: true);
        }

        // structseq_new_impl
        private static PyObject New(PyStructSequenceType type, PyObject[] args, PyDict kwargs)
        {
            PyObject sequence = args.Length > 0 ? args[0] : null, dict = args.Length > 1 ? args[1] : null;
            if (args.Length > 2)
                throw PyTypeError.Create($"structseq() takes at most 2 arguments ({args.Length} given)");
            if (kwargs != null)
                foreach (var pair in kwargs.InternalDict)
                {
                    string keyword = (pair.Key as PyString)?.Value;
                    if (keyword == "sequence" && sequence == null) sequence = pair.Value;
                    else if (keyword == "dict" && dict == null) dict = pair.Value;
                    else throw PyTypeError.Create($"'{keyword}' is an invalid keyword argument for structseq()");
                }
            if (sequence == null)
                throw PyTypeError.Create("structseq() missing required argument 'sequence' (pos 1)");
            PyObject[] items;
            if (sequence is PyTuple tuple)
                items = tuple.Items;
            else
                try
                {
                    items = PyList.FromIterable(sequence).AsTuple().Items;
                }
                catch (PythonException error) when (error.PyException is PyTypeError)
                {
                    throw PyTypeError.Create("constructor requires a sequence");
                }
            if (dict != null && dict is not PyDict)
                throw PyTypeError.Create($"{type.FullName}() takes a dict as second arg, if any");
            int minimum = type.Visible, maximum = type.Fields.Length;
            if (minimum != maximum)
            {
                if (items.Length < minimum)
                    throw PyTypeError.Create($"{type.FullName}() takes an at least {minimum}-sequence ({items.Length}-sequence given)");
                if (items.Length > maximum)
                    throw PyTypeError.Create($"{type.FullName}() takes an at most {maximum}-sequence ({items.Length}-sequence given)");
            }
            else if (items.Length != minimum)
                throw PyTypeError.Create($"{type.FullName}() takes a {minimum}-sequence ({items.Length}-sequence given)");
            var fields = new PyObject[maximum];
            int unnamed = maximum - type.Members.Length;
            for (int i = 0; i < maximum; i++)
            {
                if (i < items.Length)
                    fields[i] = items[i];
                else if (dict is PyDict named && named.ContainsKey(new PyString(type.Members[i - unnamed])))
                    fields[i] = named.GetItem(new PyString(type.Members[i - unnamed]));
                else
                    fields[i] = PyNone.Instance;
            }
            return new PyStructSequence(type, fields);
        }
    }

    /// <summary>PyStructSequence: a tuple of the visible fields, and the rest beside it.</summary>
    public class PyStructSequence : PyTuple
    {
        private readonly PyStructSequenceType _type;
        internal readonly PyObject[] AllFields;

        internal PyStructSequence(PyStructSequenceType type, PyObject[] fields)
            : base(Visible(type, fields))
        {
            _type = type;
            AllFields = fields;
        }

        private static PyObject[] Visible(PyStructSequenceType type, PyObject[] fields)
        {
            if (fields.Length == type.Visible)
                return fields;
            var visible = new PyObject[type.Visible];
            Array.Copy(fields, visible, visible.Length);
            return visible;
        }

        public override PyType GetPyType() => _type;
        public override string GetTypeName() => _type.FullName;

        // structseq_repr: the names of tp_members, so an unnamed field shows under the next name.
        public override PyString ToRepr()
        {
            var text = new System.Text.StringBuilder(_type.FullName).Append('(');
            for (int i = 0; i < _type.Visible; i++)
            {
                if (i > 0)
                    text.Append(", ");
                text.Append(_type.Members[i]).Append('=').Append(Items[i].ToRepr().Value);
            }
            return new PyString(text.Append(')').ToString());
        }

        public override PyString AsString() => ToRepr();

        // structseq_reduce
        internal PyObject Reduce()
        {
            var hidden = new PyDict();
            int unnamed = _type.Fields.Length - _type.Members.Length;
            for (int i = _type.Visible; i < _type.Fields.Length; i++)
                hidden.SetItem(new PyString(_type.Members[i - unnamed]), AllFields[i]);
            return new PyTuple(new PyObject[] { _type, new PyTuple(new PyObject[] { new PyTuple(Items), hidden }) });
        }
    }
}
