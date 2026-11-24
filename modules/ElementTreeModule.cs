using System;
using System.Collections.Generic;
using SharpPy.Modules.Expat;
using XML_Parser = SharpPy.Modules.Expat.XML_ParserStruct;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/_elementtree.c: Element, its iterator, TreeBuilder and XMLParser (over the expat of
    /// pyexpat's expat_CAPI capsule), SubElement and _set_factories. One C# method per C function.
    /// The JOIN flag C keeps in the low bit of text and tail is a bool beside each.
    /// </summary>
    internal static class ElementTreeModule
    {
        private const int STATIC_CHILDREN = 4;

        /// <summary>elementtreestate (one per process, like the other ports' module state)</summary>
        internal sealed class elementtreestate
        {
            internal PyType parseerror_obj;
            internal PyObject deepcopy_obj;
            internal PyObject elementpath_obj;
            internal PyObject comment_factory;
            internal PyObject pi_factory;
            internal readonly PyString str_text = PyString.InternFrom("text");
            internal readonly PyString str_tail = PyString.InternFrom("tail");
            internal readonly PyString str_append = PyString.InternFrom("append");
            internal readonly PyString str_find = PyString.InternFrom("find");
            internal readonly PyString str_findtext = PyString.InternFrom("findtext");
            internal readonly PyString str_findall = PyString.InternFrom("findall");
            internal readonly PyString str_iterfind = PyString.InternFrom("iterfind");
            internal readonly PyString str_doctype = PyString.InternFrom("doctype");
            internal PyType Element_Type;
            internal PyType ElementIter_Type;
            internal PyType TreeBuilder_Type;
            internal PyType XMLParser_Type;
            internal PyObject expat_capsule;
            internal PyExpat_CAPI expat_capi;
        }


        internal static PyModule Create()
        {
            var m = new PyModule("_elementtree");
            module_exec(m);
            return m;
        }

        /// <summary>list_join</summary>
        private static PyObject list_join(PyObject list) =>
            // PyUnicode_Join(joiner, list): the items of any sequence (a str is its characters)
            PyString.JoinArray(PyString.Empty, list is PyList l ? l.Items : PyList.SequenceFast(list, "can only join an iterable"));

        /// <summary>is_empty_dict</summary>
        private static bool is_empty_dict(PyObject obj) => obj is PyDict d && d.GetPyType() == PyType.DictType && d.Length() == 0;

        // ------------------------------------------------------------------ the element type

        /// <summary>ElementObjectExtra</summary>
        internal sealed class ElementObjectExtra
        {
            /* attributes (a dictionary object), or NULL if no attributes */
            internal PyObject attrib;
            /* child elements */
            internal long length; /* actual number of items */
            internal long allocated; /* allocated items */
            /* this either points to _children or to a malloced buffer */
            internal PyObject[] children;
            internal bool static_children;
        }

        /// <summary>ElementObject</summary>
        internal sealed class ElementObject : PyObject
        {
            /* element tag (a string). */
            internal PyObject tag;
            /* text before first child.  note that this is a tagged pointer;
               use JOIN_OBJ to get the object pointer.  the join flag is used
               to distinguish lists created by the tree builder from lists
               assigned to the attribute by application code; the former
               should be joined before being returned to the user, the latter
               should be left intact. */
            internal PyObject text;
            internal bool text_join;
            /* text after this element, in parent.  note that this is also
               a tagged pointer; use JOIN_OBJ to get the object pointer. */
            internal PyObject tail;
            internal bool tail_join;
            internal ElementObjectExtra extra;

            internal readonly elementtreestate st;

            internal ElementObject(PyType type, elementtreestate st)
            {
                this.st = st;
                if (!ReferenceEquals(type, st.Element_Type))
                    SetCustomType(type);
            }

            public override PyType GetPyType() => _customType ?? st.Element_Type;

            // element_repr
            protected internal override PyString Repr() => element_repr(this);

            // PyObject_GetIter without tp_iter: the type has sq_item, so a sequence iterator (PySeqIter_New)
            protected internal override PyObject Iter() => new PyGenericIterator(this);

            // element_length (sq_length and mp_length)
            protected internal override long Len() => element_length(this);

            // element_bool
            protected internal override bool Bool() => element_bool(this);

            // element_subscr
            protected internal override PyObject MpSubscript(PyObject key) => element_subscr(this, key);

            // element_ass_subscr
            protected internal override void MpAssSubscript(PyObject key, PyObject value) => element_ass_subscr(this, key, value);

            protected internal override void MpDelSubscript(PyObject key) => element_ass_subscr(this, key, null);
        }

        private static bool Element_CheckExact(elementtreestate st, PyObject op) => op.GetPyType() == st.Element_Type;

        private static bool Element_Check(elementtreestate st, PyObject op) =>
            op is ElementObject e && (e.st == st || op.GetPyType().IsSubclassOf(st.Element_Type));

        /// <summary>create_extra</summary>
        private static void create_extra(ElementObject self, PyObject attrib)
        {
            self.extra = new ElementObjectExtra
            {
                attrib = attrib,
                length = 0,
                allocated = STATIC_CHILDREN,
                children = new PyObject[STATIC_CHILDREN],
                static_children = true,
            };
        }

        /// <summary>clear_extra</summary>
        private static void clear_extra(ElementObject self)
        {
            if (self.extra == null)
                return;
            /* Avoid DECREFs calling into this code again (cycles, etc.) */
            self.extra = null;
        }

        /// <summary>create_new_element: convenience internal function to create new Element objects.</summary>
        private static PyObject create_new_element(elementtreestate st, PyObject tag, PyObject attrib)
        {
            var self = new ElementObject(st.Element_Type, st);
            self.extra = null;
            self.tag = tag;
            self.text = PyNone.Instance;
            self.tail = PyNone.Instance;
            if (attrib != null && !is_empty_dict(attrib))
                create_extra(self, attrib);
            return self;
        }

        /// <summary>element_new</summary>
        private static PyObject element_new(elementtreestate st, PyType type, PyObject[] args, PyDict kwds)
        {
            var e = new ElementObject(type, st);
            e.tag = PyNone.Instance;
            e.text = PyNone.Instance;
            e.tail = PyNone.Instance;
            e.extra = null;
            return e;
        }

        /// <summary>
        /// get_attrib_from_keywords: helper function for extracting the attrib dictionary from a
        /// keywords dict. This is required by some constructors/functions in this module that can
        /// either accept attrib as a keyword argument or all attributes splashed directly into
        /// *kwds.
        /// </summary>
        private static PyDict get_attrib_from_keywords(PyDict kwds)
        {
            PyObject attrib_str = PyString.From("attrib");
            PyObject attrib = kwds.InternalDict.TryGetValue(attrib_str, out var a) ? a : null;
            PyDict result;
            if (attrib != null)
            {
                /* If attrib was found in kwds, copy its value and remove it from
                 * kwds
                 */
                if (attrib is not PyDict attribDict)
                    throw PyTypeError.Create($"attrib must be dict, not {attrib.GetPyType().FullName}");
                result = attribDict.Copy();
                kwds.DelItem(attrib_str);
            }
            else
                result = new PyDict();
            result.Update(kwds);
            return result;
        }

        /// <summary>element_init</summary>
        private static void element_init(PyObject self, PyObject[] args, PyDict kwds)
        {
            PyObject tag;
            PyObject attrib = null;

            // PyArg_ParseTuple(args, "O|O!:Element", &tag, &PyDict_Type, &attrib)
            PyArgs.ParseTuple("Element", args, 1, 2);
            tag = args[0];
            if (args.Length > 1)
            {
                if (args[1] is not PyDict)
                    throw PyTypeError.Create($"Element() argument 2 must be dict, not {TypeName(args[1])}");
                attrib = args[1];
            }

            if (attrib != null)
            {
                /* attrib passed as positional arg */
                var copy = ((PyDict)attrib).Copy();
                if (kwds != null)
                    copy.Update(kwds);
                attrib = copy;
            }
            else if (kwds != null)
            {
                /* have keywords args */
                attrib = get_attrib_from_keywords(kwds);
            }

            ElementObject self_elem = (ElementObject)self;

            if (attrib != null && !is_empty_dict(attrib))
                create_extra(self_elem, attrib);

            /* We own a reference to attrib here and it's no longer needed. */

            /* Replace the objects already pointed to by tag, text and tail. */
            self_elem.tag = tag;
            self_elem.text = PyNone.Instance;
            self_elem.text_join = false;
            self_elem.tail = PyNone.Instance;
            self_elem.tail_join = false;
        }

        // %.100s of Py_TYPE(o)->tp_name
        private static string TypeName(PyObject o) => o is PyNone ? "None" : o.GetPyType().FullName;

        /// <summary>element_resize: make sure self->children can hold the given number of extra elements.</summary>
        private static void element_resize(ElementObject self, long extra)
        {
            long size;
            if (self.extra == null)
                create_extra(self, null);

            size = self.extra.length + extra; /* never overflows */

            if (size > self.extra.allocated)
            {
                /* use Python 2.4's list growth strategy */
                size = (size >> 3) + (size < 9 ? 3 : 6) + size;
                /* Coverity CID #182 size_error: Allocating 1 bytes to pointer "children"
                 * which needs at least 4 bytes.
                 * Although it's a false alarm always assume at least one child to
                 * be safe.
                 */
                size = size != 0 ? size : 1;
                if (size > Array.MaxLength)
                    throw PyMemoryError.Create();
                var children = new PyObject[size];
                Array.Copy(self.extra.children, children, self.extra.length);
                self.extra.children = children;
                self.extra.allocated = size;
                self.extra.static_children = false;
            }
        }

        /// <summary>raise_type_error</summary>
        private static Exception raise_type_error(PyObject element) =>
            PyTypeError.Create($"expected an Element, not \"{TypeName(element)}\"");

        /// <summary>element_add_subelement</summary>
        private static void element_add_subelement(elementtreestate st, ElementObject self, PyObject element)
        {
            /* add a child element to a parent */
            if (!Element_Check(st, element))
                throw raise_type_error(element);

            element_resize(self, 1);

            self.extra.children[self.extra.length] = element;

            self.extra.length++;
        }

        /// <summary>element_get_attrib: return borrowed reference to attrib dictionary (must have extra).</summary>
        private static PyObject element_get_attrib(ElementObject self)
        {
            PyObject res = self.extra.attrib;
            if (res == null)
            {
                /* create missing dictionary */
                res = self.extra.attrib = new PyDict();
            }
            return res;
        }

        /// <summary>element_get_text: return borrowed reference to text attribute</summary>
        private static PyObject element_get_text(ElementObject self)
        {
            PyObject res = self.text;
            if (self.text_join)
            {
                if (res is PyList { IsExact: true })
                {
                    PyObject tmp = list_join(res);
                    self.text = tmp;
                    self.text_join = false;
                    res = tmp;
                }
            }
            return res;
        }

        /// <summary>element_get_tail: return borrowed reference to text attribute</summary>
        private static PyObject element_get_tail(ElementObject self)
        {
            PyObject res = self.tail;
            if (self.tail_join)
            {
                if (res is PyList { IsExact: true })
                {
                    PyObject tmp = list_join(res);
                    self.tail = tmp;
                    self.tail_join = false;
                    res = tmp;
                }
            }
            return res;
        }

        /// <summary>subelement</summary>
        private static PyObject subelement(elementtreestate st, PyObject[] args, PyDict kwds)
        {
            ElementObject parent;
            PyObject tag;
            PyObject attrib = null;

            // PyArg_ParseTuple(args, "O!O|O!:SubElement", st->Element_Type, &parent, &tag, &PyDict_Type, &attrib)
            PyArgs.ParseTuple("SubElement", args, 2, 3);
            if (!Element_Check(st, args[0]) || args[0] is not ElementObject p)
                throw PyTypeError.Create($"SubElement() argument 1 must be {st.Element_Type.FullName}, not {TypeName(args[0])}");
            parent = p;
            tag = args[1];
            if (args.Length > 2)
            {
                if (args[2] is not PyDict)
                    throw PyTypeError.Create($"SubElement() argument 3 must be dict, not {TypeName(args[2])}");
                attrib = args[2];
            }

            if (attrib != null)
            {
                /* attrib passed as positional arg */
                var copy = ((PyDict)attrib).Copy();
                if (kwds != null)
                    copy.Update(kwds);
                attrib = copy;
            }
            else if (kwds != null && kwds.Length() != 0)
            {
                /* have keyword args */
                attrib = get_attrib_from_keywords(kwds);
            }
            else
            {
                /* no attrib arg, no kwds, so no attribute */
            }

            PyObject elem = create_new_element(st, tag, attrib);
            element_add_subelement(st, parent, elem);
            return elem;
        }

        /// <summary>_elementtree_Element_append_impl</summary>
        private static PyObject Element_append(ElementObject self, PyObject subelement)
        {
            element_add_subelement(self.st, self, subelement);
            return PyNone.Instance;
        }

        /// <summary>_elementtree_Element_clear_impl</summary>
        private static PyObject Element_clear(ElementObject self)
        {
            clear_extra(self);

            self.text = PyNone.Instance;
            self.text_join = false;
            self.tail = PyNone.Instance;
            self.tail_join = false;

            return PyNone.Instance;
        }

        /// <summary>_elementtree_Element___copy___impl</summary>
        private static PyObject Element___copy__(ElementObject self)
        {
            elementtreestate st = self.st;
            ElementObject element = (ElementObject)create_new_element(st, self.tag, self.extra?.attrib);

            element.text = self.text;
            element.text_join = self.text_join;
            element.tail = self.tail;
            element.tail_join = self.tail_join;

            if (self.extra != null)
            {
                element_resize(element, self.extra.length);
                for (long i = 0; i < self.extra.length; i++)
                    element.extra.children[i] = self.extra.children[i];
                element.extra.length = self.extra.length;
            }

            return element;
        }

        /// <summary>_elementtree_Element___deepcopy___impl</summary>
        private static PyObject Element___deepcopy__(ElementObject self, PyObject memo)
        {
            ElementObject element;
            PyObject tag;
            PyObject attrib;
            PyObject text;
            PyObject tail;
            elementtreestate st = self.st;

            tag = deepcopy(st, self.tag, memo);

            if (self.extra != null && self.extra.attrib != null)
                attrib = deepcopy(st, self.extra.attrib, memo);
            else
                attrib = null;

            element = (ElementObject)create_new_element(st, tag, attrib);

            text = deepcopy(st, self.text, memo);
            element.text = text;
            element.text_join = self.text_join;

            tail = deepcopy(st, self.tail, memo);
            element.tail = tail;
            element.tail_join = self.tail_join;

            if (self.extra != null)
            {
                element_resize(element, self.extra.length);

                // TODO(picnixz): check for an evil child's __deepcopy__ on 'self'
                for (long i = 0; i < self.extra.length; i++)
                {
                    PyObject child = deepcopy(st, self.extra.children[i], memo);
                    if (child == null || !Element_Check(st, child))
                    {
                        element.extra.length = i;
                        throw raise_type_error(child);
                    }
                    element.extra.children[i] = child;
                }

                element.extra.length = self.extra.length;
            }

            /* add object to memo dictionary (so deepcopy won't visit it again) */
            PyObject id = PyInt.From(PyObjectId.Of(self));
            PyAbstract.SetItem(memo, id, element);

            return element;
        }

        /// <summary>
        /// deepcopy: the C function copies a dict of str or an exact Element only it holds itself
        /// (Py_REFCNT(object) == 1); the CLR has no reference count, so everything but None and an
        /// exact str goes to copy.deepcopy, which gives an equal copy.
        /// </summary>
        private static PyObject deepcopy(elementtreestate st, PyObject @object, PyObject memo)
        {
            /* Fast paths */
            if (@object is PyNone || @object is PyString { IsExact: true })
                return @object;

            if (st.deepcopy_obj == null)
                throw PyRuntimeError.Create("deepcopy helper not found");

            return st.deepcopy_obj.Call(new[] { @object, memo }, null);
        }

        /// <summary>_elementtree_Element___sizeof___impl</summary>
        private static long Element___sizeof__(ElementObject self)
        {
            long result = self.GetPyType().Layout.BasicSize;
            if (self.extra != null)
            {
                result += 64; // sizeof(ElementObjectExtra)
                if (!self.extra.static_children)
                    result += self.extra.allocated * 8;
            }
            return result;
        }

        private const string PICKLED_TAG = "tag";
        private const string PICKLED_CHILDREN = "_children";
        private const string PICKLED_ATTRIB = "attrib";
        private const string PICKLED_TAIL = "tail";
        private const string PICKLED_TEXT = "text";

        /// <summary>_elementtree_Element___getstate___impl</summary>
        private static PyObject Element___getstate__(ElementObject self)
        {
            long n = self.extra != null ? self.extra.length : 0;
            var children = new PyObject[n];
            for (long i = 0; i < n; i++)
                children[i] = self.extra.children[i];

            PyObject attrib;
            if (self.extra != null && self.extra.attrib != null)
                attrib = self.extra.attrib;
            else
                attrib = new PyDict();

            var d = new PyDict();
            d.SetItem(PyString.From(PICKLED_TAG), self.tag);
            d.SetItem(PyString.From(PICKLED_CHILDREN), new PyList(children));
            d.SetItem(PyString.From(PICKLED_ATTRIB), attrib);
            d.SetItem(PyString.From(PICKLED_TEXT), self.text);
            d.SetItem(PyString.From(PICKLED_TAIL), self.tail);
            return d;
        }

        /// <summary>element_setstate_from_attributes</summary>
        private static PyObject element_setstate_from_attributes(elementtreestate st, ElementObject self, PyObject tag,
                                                                 PyObject attrib, PyObject text, PyObject tail,
                                                                 PyObject children)
        {
            long nchildren;
            ElementObjectExtra oldextra = null;

            if (tag == null)
                throw PyTypeError.Create("tag may not be NULL");

            self.tag = tag;

            if (text != null)
            {
                self.text = text;
                self.text_join = text is PyList { IsExact: true };
            }
            else
            {
                self.text = PyNone.Instance;
                self.text_join = false;
            }

            if (tail != null)
            {
                self.tail = tail;
                self.tail_join = tail is PyList { IsExact: true };
            }
            else
            {
                self.tail = PyNone.Instance;
                self.tail_join = false;
            }

            /* Handle ATTRIB and CHILDREN. */
            if (children == null && attrib == null)
                return PyNone.Instance;

            /* Compute 'nchildren'. */
            if (children != null)
            {
                if (children is not PyList childList)
                    throw PyTypeError.Create("'_children' is not a list");
                PyObject[] items = childList.Items;
                nchildren = items.Length;

                /* (Re-)allocate 'extra'.
                   Avoid DECREFs calling into this code again (cycles, etc.)
                 */
                oldextra = self.extra;
                self.extra = null;
                element_resize(self, nchildren);
                if (oldextra != null)
                {
                    self.extra.attrib = oldextra.attrib;
                    oldextra.attrib = null;
                }

                /* Copy children */
                for (long i = 0; i < nchildren; i++)
                {
                    PyObject child = items[i];
                    if (!Element_Check(st, child))
                    {
                        self.extra.length = i;
                        throw raise_type_error(child);
                    }
                    self.extra.children[i] = child;
                }

                self.extra.length = nchildren;
            }
            else
                element_resize(self, 0);

            /* Stash attrib. */
            self.extra.attrib = attrib;

            return PyNone.Instance;
        }

        /// <summary>
        /// element_setstate_from_Python: __setstate__ for Element instance from the Python
        /// implementation. 'state' should be the instance dict.
        /// </summary>
        private static PyObject element_setstate_from_Python(elementtreestate st, ElementObject self, PyDict state)
        {
            string[] kwlist = { PICKLED_TAG, PICKLED_ATTRIB, PICKLED_TEXT, PICKLED_TAIL, PICKLED_CHILDREN };
            PyObject tag = null, attrib = null, text = null, tail = null, children = null;

            // PyArg_ParseTupleAndKeywords(args, state, "|$OOOOO", kwlist, ...)
            foreach (var pair in state.InternalDict)
            {
                if (pair.Key is not PyString key)
                    throw PyTypeError.Create("keywords must be strings");
                switch (key.Value)
                {
                    case PICKLED_TAG: tag = pair.Value; break;
                    case PICKLED_ATTRIB: attrib = pair.Value; break;
                    case PICKLED_TEXT: text = pair.Value; break;
                    case PICKLED_TAIL: tail = pair.Value; break;
                    case PICKLED_CHILDREN: children = pair.Value; break;
                    default:
                        throw PyTypeError.Create($"'{key.Value}' is an invalid keyword argument for this function");
                }
            }
            return element_setstate_from_attributes(st, self, tag, attrib, text, tail, children);
        }

        /// <summary>_elementtree_Element___setstate___impl</summary>
        private static PyObject Element___setstate__(ElementObject self, PyObject state)
        {
            if (state is not PyDict d || d.GetPyType() != PyType.DictType)
                throw PyTypeError.Create($"Don't know how to unpickle \"{state.ToRepr().Value}\" as an Element");
            return element_setstate_from_Python(self.st, self, d);
        }

        /// <summary>
        /// checkpath: check if a tag contains an xpath character. A tag that starts with "{}"
        /// or "{*}" is a path for ElementPath.
        /// </summary>
        private static bool checkpath(PyObject tag)
        {
            bool check = true;

            static bool PATHCHAR(int ch) => ch == '/' || ch == '*' || ch == '[' || ch == '@' || ch == '.';

            if (tag is PyString s)
            {
                int[] data = s.ToCodePoints();
                int len = data.Length;
                if (len >= 3 && data[0] == '{' && (data[1] == '}' || (data[1] == '*' && data[2] == '}')))
                {
                    /* wildcard: '{}tag' or '{*}tag' */
                    return true;
                }
                for (int i = 0; i < len; i++)
                {
                    int ch = data[i];
                    if (ch == '{')
                        check = false;
                    else if (ch == '}')
                        check = true;
                    else if (check && PATHCHAR(ch))
                        return true;
                }
                return false;
            }
            if (tag is PyBytes b)
            {
                byte[] p = b.Value;
                int len = p.Length;
                if (len >= 3 && p[0] == '{' && (p[1] == '}' || (p[1] == '*' && p[2] == '}')))
                {
                    /* wildcard: '{}tag' or '{*}tag' */
                    return true;
                }
                for (int i = 0; i < len; i++)
                {
                    if (p[i] == '{')
                        check = false;
                    else if (p[i] == '}')
                        check = true;
                    else if (check && PATHCHAR(p[i]))
                        return true;
                }
                return false;
            }

            return true; /* unknown type; might be path expression */
        }

        /// <summary>_elementtree_Element_extend_impl</summary>
        private static PyObject Element_extend(ElementObject self, PyObject elements)
        {
            PyObject[] seq = PyList.SequenceFast(elements, "'elements' must be an iterable");
            elementtreestate st = self.st;
            for (int i = 0; i < seq.Length; i++)
                element_add_subelement(st, self, seq[i]);
            return PyNone.Instance;
        }

        private static PyObject CallMethod(PyObject obj, PyString name, params PyObject[] args) =>
            obj.GetAttribute(name.Value).Call(args, null);

        /// <summary>_elementtree_Element_find_impl</summary>
        private static PyObject Element_find(ElementObject self, PyObject path, PyObject namespaces)
        {
            elementtreestate st = self.st;

            if (checkpath(path) || namespaces is not PyNone)
                return CallMethod(st.elementpath_obj, st.str_find, self, path, namespaces);

            for (long i = 0; self.extra != null && i < self.extra.length; i++)
            {
                PyObject item = self.extra.children[i];
                PyObject tag = ((ElementObject)item).tag;
                bool rc = PyObject.CompareBool(tag, path, PyObject.CompareOp.EQ);
                if (rc)
                    return item;
            }

            return PyNone.Instance;
        }

        /// <summary>_elementtree_Element_findtext_impl</summary>
        private static PyObject Element_findtext(ElementObject self, PyObject path, PyObject default_value, PyObject namespaces)
        {
            elementtreestate st = self.st;

            if (checkpath(path) || namespaces is not PyNone)
                return CallMethod(st.elementpath_obj, st.str_findtext, self, path, default_value, namespaces);

            for (long i = 0; self.extra != null && i < self.extra.length; i++)
            {
                PyObject item = self.extra.children[i];
                PyObject tag = ((ElementObject)item).tag;
                bool rc = PyObject.CompareBool(tag, path, PyObject.CompareOp.EQ);
                if (rc)
                {
                    PyObject text = element_get_text((ElementObject)item);
                    if (text is PyNone)
                        return PyString.Empty;
                    return text;
                }
            }

            return default_value;
        }

        /// <summary>_elementtree_Element_findall_impl</summary>
        private static PyObject Element_findall(ElementObject self, PyObject path, PyObject namespaces)
        {
            elementtreestate st = self.st;

            if (checkpath(path) || namespaces is not PyNone)
                return CallMethod(st.elementpath_obj, st.str_findall, self, path, namespaces);

            var @out = new PyList();
            for (long i = 0; self.extra != null && i < self.extra.length; i++)
            {
                PyObject item = self.extra.children[i];
                PyObject tag = ((ElementObject)item).tag;
                bool rc = PyObject.CompareBool(tag, path, PyObject.CompareOp.EQ);
                if (rc)
                    @out.Append(item);
            }

            return @out;
        }

        /// <summary>_elementtree_Element_iterfind_impl</summary>
        private static PyObject Element_iterfind(ElementObject self, PyObject path, PyObject namespaces)
        {
            PyObject tag = path;
            elementtreestate st = self.st;
            return CallMethod(st.elementpath_obj, st.str_iterfind, self, tag, namespaces);
        }

        /// <summary>_elementtree_Element_get_impl</summary>
        private static PyObject Element_get(ElementObject self, PyObject key, PyObject default_value)
        {
            if (self.extra != null && self.extra.attrib != null)
            {
                PyObject attrib = self.extra.attrib;
                PyObject value = DictGetItemWithError(attrib, key);
                if (value != null)
                    return value;
            }
            return default_value;
        }

        // PyDict_GetItemWithError: an error of the key's __hash__ or __eq__ propagates.
        private static PyObject DictGetItemWithError(PyObject dict, PyObject key) =>
            ((PyDict)dict).InternalDict.TryGetValue(key, out var value) ? value : null;

        /// <summary>_elementtree_Element_iter_impl</summary>
        private static PyObject Element_iter(ElementObject self, PyObject tag)
        {
            if (tag is PyString s)
            {
                if (s.Value == "*")
                    tag = PyNone.Instance;
            }
            else if (tag is PyBytes b)
            {
                if (b.Value.Length == 1 && b.Value[0] == '*')
                    tag = PyNone.Instance;
            }
            return create_elementiter(self.st, self, tag, false);
        }

        /// <summary>_elementtree_Element_itertext_impl</summary>
        private static PyObject Element_itertext(ElementObject self) =>
            create_elementiter(self.st, self, PyNone.Instance, true);

        /// <summary>element_getitem</summary>
        private static PyObject element_getitem(ElementObject self, long index)
        {
            if (self.extra == null || index < 0 || index >= self.extra.length)
                throw PyIndexError.Create("child index out of range");
            return self.extra.children[index];
        }

        /// <summary>element_bool</summary>
        private static bool element_bool(ElementObject self)
        {
            PyWarnings.Warn(PyType.DeprecationWarningType,
                            "Testing an element's truth value will always return True in future versions.  "
                            + "Use specific 'len(elem)' or 'elem is not None' test instead.", 1);
            return (self.extra != null ? self.extra.length : 0) != 0;
        }

        /// <summary>_elementtree_Element_insert_impl</summary>
        private static PyObject Element_insert(ElementObject self, long index, PyObject subelement)
        {
            if (self.extra == null)
                create_extra(self, null);

            if (index < 0)
            {
                index += self.extra.length;
                if (index < 0)
                    index = 0;
            }
            if (index > self.extra.length)
                index = self.extra.length;

            element_resize(self, 1);

            for (long i = self.extra.length; i > index; i--)
                self.extra.children[i] = self.extra.children[i - 1];

            self.extra.children[index] = subelement;

            self.extra.length++;

            return PyNone.Instance;
        }

        /// <summary>_elementtree_Element_items_impl</summary>
        private static PyObject Element_items(ElementObject self)
        {
            if (self.extra == null || self.extra.attrib == null)
                return new PyList();
            return ((PyDict)self.extra.attrib).Items();
        }

        /// <summary>_elementtree_Element_keys_impl</summary>
        private static PyObject Element_keys(ElementObject self)
        {
            if (self.extra == null || self.extra.attrib == null)
                return new PyList();
            return ((PyDict)self.extra.attrib).Keys();
        }

        /// <summary>element_length</summary>
        private static long element_length(ElementObject self)
        {
            if (self.extra == null)
                return 0;
            return self.extra.length;
        }

        /// <summary>_elementtree_Element_makeelement_impl</summary>
        private static PyObject Element_makeelement(ElementObject self, PyObject tag, PyObject attrib)
        {
            attrib = ((PyDict)attrib).Copy();
            return create_new_element(self.st, tag, attrib);
        }

        /// <summary>_elementtree_Element_remove_impl</summary>
        private static PyObject Element_remove(ElementObject self, PyObject subelement)
        {
            long i;
            // When iterating over the list of children, we need to check that the
            // list is not cleared (self->extra != NULL) and that we are still within
            // the correct bounds (i < self->extra->length).
            //
            // We deliberately avoid protecting against children lists that grow
            // faster than the index since list objects do not protect against it.
            bool rc = false;
            for (i = 0; self.extra != null && i < self.extra.length; i++)
            {
                if (self.extra.children[i] == subelement)
                {
                    rc = true;
                    break;
                }
                PyObject child = self.extra.children[i];
                rc = PyObject.CompareBool(child, subelement, PyObject.CompareOp.EQ);
                if (rc)
                    break;
            }

            if (!rc)
                throw PyValueError.Create("list.remove(x): x not in list");

            // An extra check must be done if the mutation occurs at the very last
            // step and removes or clears the 'extra' list (the condition on the
            // length would not be satisfied any more).
            if (self.extra == null || i >= self.extra.length)
                return PyNone.Instance;

            self.extra.length--;
            for (; i < self.extra.length; i++)
                self.extra.children[i] = self.extra.children[i + 1];
            self.extra.children[self.extra.length] = null;

            return PyNone.Instance;
        }

        /// <summary>element_repr</summary>
        private static PyString element_repr(ElementObject self)
        {
            if (self.tag == null)
                return PyString.From($"<Element at {PyObjectId.Pointer(self)}>");

            if (PyReprGuard.Enter(self))
            {
                try
                {
                    return PyString.From($"<Element {self.tag.ToRepr().Value} at {PyObjectId.Pointer(self)}>");
                }
                finally
                {
                    PyReprGuard.Leave(self);
                }
            }
            throw PyRuntimeError.Create($"reentrant call inside {self.GetPyType().FullName}.__repr__");
        }

        /// <summary>_elementtree_Element_set_impl</summary>
        private static PyObject Element_set(ElementObject self, PyObject key, PyObject value)
        {
            if (self.extra == null)
                create_extra(self, null);

            PyObject attrib = element_get_attrib(self);
            PyAbstract.SetItem(attrib, key, value);
            return PyNone.Instance;
        }

        /// <summary>element_setitem</summary>
        private static void element_setitem(ElementObject self, long index, PyObject item)
        {
            if (self.extra == null || index < 0 || index >= self.extra.length)
                throw PyIndexError.Create("child assignment index out of range");

            if (item != null)
            {
                elementtreestate st = self.st;
                if (!Element_Check(st, item))
                    throw raise_type_error(item);
                self.extra.children[index] = item;
            }
            else
            {
                self.extra.length--;
                for (long i = index; i < self.extra.length; i++)
                    self.extra.children[i] = self.extra.children[i + 1];
                self.extra.children[self.extra.length] = null;
            }
        }

        /// <summary>element_subscr</summary>
        private static PyObject element_subscr(ElementObject self, PyObject item)
        {
            if (PyObject.IndexCheck(item))
            {
                long i = PyArgs.AsSsize(item, PyArgs.SsizeError.Index);
                if (i < 0 && self.extra != null)
                    i += self.extra.length;
                return element_getitem(self, i);
            }
            else if (item is PySlice slice)
            {
                if (self.extra == null)
                    return new PyList();

                var (start, stop, step) = slice.UnpackStorage();
                (start, stop, step) = PySlice.AdjustStorageIndices((int)self.extra.length, start, stop, step);
                int slicelen = PySlice.GetLength(start, stop, step);

                if (slicelen <= 0)
                    return new PyList();
                else
                {
                    var list = new PyObject[slicelen];
                    long cur = start;
                    for (int i = 0; i < slicelen; cur += step, i++)
                        list[i] = self.extra.children[cur];
                    return new PyList(list);
                }
            }
            else
                throw PyTypeError.Create("element indices must be integers");
        }

        /// <summary>element_ass_subscr</summary>
        private static void element_ass_subscr(ElementObject self, PyObject item, PyObject value)
        {
            if (PyObject.IndexCheck(item))
            {
                long i = PyArgs.AsSsize(item, PyArgs.SsizeError.Index);
                if (i < 0 && self.extra != null)
                    i += self.extra.length;
                element_setitem(self, i, value);
                return;
            }
            else if (item is PySlice slice)
            {
                int start, stop, step, slicelen, newlen;
                long cur;

                if (self.extra == null)
                    create_extra(self, null);

                (start, stop, step) = slice.UnpackStorage();
                (start, stop, step) = PySlice.AdjustStorageIndices((int)self.extra.length, start, stop, step);
                slicelen = PySlice.GetLength(start, stop, step);

                if (value == null)
                {
                    /* Delete slice */
                    if (slicelen <= 0)
                        return;

                    /* Since we're deleting, the direction of the range doesn't matter,
                     * so for simplicity make it always ascending.
                    */
                    if (step < 0)
                    {
                        stop = start + 1;
                        start = stop + step * (slicelen - 1) - 1;
                        step = -step;
                    }

                    PyObject[] children = self.extra.children;
                    int i;
                    /* At this point start is the first index to delete and step is the step
                     * between indices; stop is one past the last.
                     */
                    for (cur = start, i = 0; cur < stop; cur += step, ++i)
                    {
                        long num_moved = step - 1;
                        /* Compute how many children have to be moved, clipping at the
                         * list end.
                        */
                        if (cur + step >= self.extra.length)
                            num_moved = self.extra.length - cur - 1;

                        /* Move the children over */
                        Array.Copy(children, cur + 1, children, cur - i, num_moved);
                    }

                    /* Leftover "tail" after the last removed child */
                    cur = start + (long)slicelen * step;
                    if (cur < self.extra.length)
                        Array.Copy(children, cur, children, cur - slicelen, self.extra.length - cur);

                    self.extra.length -= slicelen;
                    for (long k = self.extra.length; k < self.extra.length + slicelen && k < children.Length; k++)
                        children[k] = null;
                    return;
                }

                /* A new slice is actually being assigned */
                PyObject[] seq = PyList.SequenceFast(value, "assignment expects an iterable");
                newlen = seq.Length;

                if (step != 1 && newlen != slicelen)
                    throw PyValueError.Create($"attempt to assign sequence of size {newlen} to extended slice of size {slicelen}");

                /* Resize before creating the recycle bin, to prevent refleaks. */
                if (newlen > slicelen)
                    element_resize(self, newlen - slicelen);

                elementtreestate st = self.st;
                for (int i = 0; i < newlen; i++)
                {
                    PyObject element = seq[i];
                    if (!Element_Check(st, element))
                        throw raise_type_error(element);
                }

                if (newlen < slicelen)
                {
                    /* delete slice */
                    for (long i = stop; i < self.extra.length; i++)
                        self.extra.children[i + newlen - slicelen] = self.extra.children[i];
                }
                else if (newlen > slicelen)
                {
                    /* insert slice */
                    for (long i = self.extra.length - 1; i >= stop; i--)
                        self.extra.children[i + newlen - slicelen] = self.extra.children[i];
                }

                /* replace the slice */
                cur = start;
                for (int i = 0; i < newlen; cur += step, i++)
                    self.extra.children[cur] = seq[i];

                self.extra.length += newlen - slicelen;
                return;
            }
            else
                throw PyTypeError.Create("element indices must be integers");
        }

        /// <summary>element_tag_getter / element_text_getter / element_tail_getter / element_attrib_getter</summary>
        private static PyObject element_attrib_getter(ElementObject self)
        {
            if (self.extra == null)
                create_extra(self, null);
            return element_get_attrib(self);
        }

        // _VALIDATE_ATTR_VALUE
        private static void _VALIDATE_ATTR_VALUE(PyObject v)
        {
            if (v == null)
                throw PyAttributeError.Create("can't delete element attribute");
        }

        /// <summary>element_attrib_setter</summary>
        private static void element_attrib_setter(ElementObject self, PyObject value)
        {
            _VALIDATE_ATTR_VALUE(value);
            if (value is not PyDict)
                throw PyTypeError.Create($"attrib must be dict, not {TypeName(value)}");
            if (self.extra == null)
                create_extra(self, null);
            self.extra.attrib = value;
        }

        private static ElementObject E(PyObject o) => (ElementObject)o;

        private static PyType MakeElementType(elementtreestate st)
        {
            var type = new PyType("Element", new[] { PyType.ObjectType }, "xml.etree.ElementTree");
            type.MarkSpecHeapType();
            type.MarkImmutableType();
            type.TypeDict["__module__"] = PyString.From("xml.etree.ElementTree");
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => element_new(st, (PyType)cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                element_init(self, args, kwargs != null && kwargs.Length() != 0 ? kwargs.Copy() : null);
                return PyNone.Instance;
            });
            TypeTables.GenericAttributes(type);

            // element_methods
            string elementName = "xml.etree.ElementTree.Element";
            PyArgs.Define(type, "clear", PyCall.NoArgs, (o, args) => Element_clear(E(o)));
            type.TypeDict["get"] = new PyMethodDescriptor("get", type, (o, args, kwargs) =>
            {
                var a = PyArgs.Unpack("get", args, kwargs, new[] { "key", "default" }, 0, 1, 2);
                return Element_get(E(o), a[0], a[1] ?? PyNone.Instance);
            }, acceptsKwargs: true);
            type.TypeDict["set"] = new PyMethodDescriptor("set", type, (o, args, kwargs) =>
            {
                PyArgs.Positional("set", args, kwargs, 2, 2);
                return Element_set(E(o), args[0], args[1]);
            }, acceptsKwargs: true);
            type.TypeDict["find"] = new PyMethodDescriptor("find", type, (o, args, kwargs) =>
            {
                var a = PyArgs.Unpack("find", args, kwargs, new[] { "path", "namespaces" }, 0, 1, 2);
                return Element_find(E(o), a[0], a[1] ?? PyNone.Instance);
            }, acceptsKwargs: true);
            type.TypeDict["findtext"] = new PyMethodDescriptor("findtext", type, (o, args, kwargs) =>
            {
                var a = PyArgs.Unpack("findtext", args, kwargs, new[] { "path", "default", "namespaces" }, 0, 1, 3);
                return Element_findtext(E(o), a[0], a[1] ?? PyNone.Instance, a[2] ?? PyNone.Instance);
            }, acceptsKwargs: true);
            type.TypeDict["findall"] = new PyMethodDescriptor("findall", type, (o, args, kwargs) =>
            {
                var a = PyArgs.Unpack("findall", args, kwargs, new[] { "path", "namespaces" }, 0, 1, 2);
                return Element_findall(E(o), a[0], a[1] ?? PyNone.Instance);
            }, acceptsKwargs: true);
            PyArgs.Define(type, "append", PyCall.Defining, (o, args) =>
            {
                if (!Element_Check(st, args[0]))
                    throw PyArgs.BadArgument("append", "argument 1", elementName, args[0]);
                return Element_append(E(o), args[0]);
            }, 1, 1);
            PyArgs.Define(type, "extend", PyCall.Defining, (o, args) => Element_extend(E(o), args[0]), 1, 1);
            type.TypeDict["insert"] = new PyMethodDescriptor("insert", type, (o, args, kwargs) =>
            {
                PyArgs.Positional("insert", args, kwargs, 2, 2);
                long index = PyArgs.LongAsSsize(PyObject.IndexOrThrow(args[0]));
                if (!Element_Check(st, args[1]))
                    throw PyArgs.BadArgument("insert", "argument 2", elementName, args[1]);
                return Element_insert(E(o), index, args[1]);
            }, acceptsKwargs: true);
            PyArgs.Define(type, "remove", PyCall.One, (o, args) =>
            {
                if (!Element_Check(st, args[0]))
                    throw PyArgs.BadArgument("remove", "argument", elementName, args[0]);
                return Element_remove(E(o), args[0]);
            });
            type.TypeDict["iter"] = new PyMethodDescriptor("iter", type, (o, args, kwargs) =>
            {
                var a = PyArgs.Unpack("iter", args, kwargs, new[] { "tag" }, 0, 0, 1);
                return Element_iter(E(o), a[0] ?? PyNone.Instance);
            }, acceptsKwargs: true);
            PyArgs.Define(type, "itertext", PyCall.Defining, (o, args) => Element_itertext(E(o)), 0, 0);
            type.TypeDict["iterfind"] = new PyMethodDescriptor("iterfind", type, (o, args, kwargs) =>
            {
                var a = PyArgs.Unpack("iterfind", args, kwargs, new[] { "path", "namespaces" }, 0, 1, 2);
                return Element_iterfind(E(o), a[0], a[1] ?? PyNone.Instance);
            }, acceptsKwargs: true);
            PyArgs.Define(type, "items", PyCall.NoArgs, (o, args) => Element_items(E(o)));
            PyArgs.Define(type, "keys", PyCall.NoArgs, (o, args) => Element_keys(E(o)));
            PyArgs.Define(type, "makeelement", PyCall.Defining, (o, args) =>
            {
                if (args[1] is not PyDict)
                    throw PyArgs.BadArgument("makeelement", "argument 2", "dict", args[1]);
                return Element_makeelement(E(o), args[0], args[1]);
            }, 2, 2);
            PyArgs.Define(type, "__copy__", PyCall.Defining, (o, args) => Element___copy__(E(o)), 0, 0);
            PyArgs.Define(type, "__deepcopy__", PyCall.One, (o, args) =>
            {
                if (args[0] is not PyDict)
                    throw PyArgs.BadArgument("__deepcopy__", "argument", "dict", args[0]);
                return Element___deepcopy__(E(o), args[0]);
            });
            PyArgs.Define(type, "__sizeof__", PyCall.NoArgs, (o, args) => PyInt.From(Element___sizeof__(E(o))));
            PyArgs.Define(type, "__getstate__", PyCall.NoArgs, (o, args) => Element___getstate__(E(o)));
            PyArgs.Define(type, "__setstate__", PyCall.Defining, (o, args) => Element___setstate__(E(o), args[0]), 1, 1);

            // element_getsetlist
            type.TypeDict["tag"] = new PyGetSetDescriptor("tag", type, o => E(o).tag,
                (o, v) => { _VALIDATE_ATTR_VALUE(v); E(o).tag = v; },
                "A string identifying what kind of data this element represents",
                o => _VALIDATE_ATTR_VALUE(null));
            type.TypeDict["text"] = new PyGetSetDescriptor("text", type, o => element_get_text(E(o)),
                (o, v) => { _VALIDATE_ATTR_VALUE(v); E(o).text = v; E(o).text_join = false; },
                "A string of text directly after the start tag, or None",
                o => _VALIDATE_ATTR_VALUE(null));
            type.TypeDict["tail"] = new PyGetSetDescriptor("tail", type, o => element_get_tail(E(o)),
                (o, v) => { _VALIDATE_ATTR_VALUE(v); E(o).tail = v; E(o).tail_join = false; },
                "A string of text directly after the end tag, or None",
                o => _VALIDATE_ATTR_VALUE(null));
            type.TypeDict["attrib"] = new PyGetSetDescriptor("attrib", type, o => element_attrib_getter(E(o)),
                (o, v) => element_attrib_setter(E(o), v),
                "A dictionary containing the element's attributes",
                o => element_attrib_setter(E(o), null));
            return type;
        }

        // ------------------------------------------------------------------ the element iterator

        /// <summary>ParentLocator</summary>
        private struct ParentLocator
        {
            internal ElementObject parent;
            internal long child_index;
        }

        /// <summary>ElementIterObject</summary>
        private sealed class ElementIterObject : PyObject
        {
            internal ParentLocator[] parent_stack;
            internal long parent_stack_used;
            internal long parent_stack_size;
            internal ElementObject root_element;
            internal PyObject sought_tag;
            internal bool gettext;

            internal elementtreestate st;

            public override PyType GetPyType() => st.ElementIter_Type;

            // PyObject_SelfIter
            protected internal override PyObject Iter() => this;

            // elementiter_next: null where C returns NULL without an error (exhausted)
            protected internal override PyObject IterNext() => elementiter_next(this) ?? throw PyStopIteration.Create();
        }

        /// <summary>parent_stack_push_new</summary>
        private static void parent_stack_push_new(ElementIterObject it, ElementObject parent)
        {
            if (it.parent_stack_used >= it.parent_stack_size)
            {
                long new_size = it.parent_stack_size * 2; /* never overflow */
                Array.Resize(ref it.parent_stack, (int)new_size);
                it.parent_stack_size = new_size;
            }
            ref ParentLocator item = ref it.parent_stack[it.parent_stack_used++];
            /* Note that extra condition on it->parent_stack_used below */
            item.parent = parent;
            item.child_index = 0;
        }

        /// <summary>elementiter_next</summary>
        private static PyObject elementiter_next(ElementIterObject it)
        {
            /* Sub-element iterator.
             *
             * A short note on gettext: this function serves both the iter() and
             * itertext() methods to avoid code duplication. However, there are a few
             * small differences in the way these iterations work. Namely:
             *   - itertext() only yields text from nodes that have it, and continues
             *     iterating when a node doesn't have text (so it doesn't return any
             *     node like iter())
             *   - itertext() also has to handle tail, after finishing with all the
             *     children of a node.
             */
            ElementObject elem;
            PyObject text;

            while (true)
            {
                /* Handle the case reached in the beginning and end of iteration, where
                 * the parent stack is empty. If root_element is NULL and we're here, the
                 * iterator is exhausted.
                 */
                if (it.parent_stack_used == 0)
                {
                    if (it.root_element == null)
                        return null;

                    elem = it.root_element; /* steals a reference */
                    it.root_element = null;
                }
                else
                {
                    /* See if there are children left to traverse in the current parent. If
                     * yes, visit the next child. If not, pop the stack and try again.
                     */
                    ref ParentLocator item = ref it.parent_stack[it.parent_stack_used - 1];
                    long child_index = item.child_index;
                    ElementObjectExtra extra;
                    elem = item.parent;
                    extra = elem.extra;
                    if (extra == null || child_index >= extra.length)
                    {
                        it.parent_stack_used--;
                        /* Note that extra condition on it->parent_stack_used here;
                         * this is because itertext() is supposed to only return *inner*
                         * text, not text following the element it began iteration with.
                         */
                        if (it.gettext && it.parent_stack_used != 0)
                        {
                            text = element_get_tail(elem);
                            goto gettext;
                        }
                        continue;
                    }

                    elem = (ElementObject)extra.children[child_index];
                    item.child_index++;
                }

                parent_stack_push_new(it, elem);
                if (it.gettext)
                {
                    text = element_get_text(elem);
                    goto gettext;
                }

                if (it.sought_tag is PyNone)
                    return elem;

                if (PyObject.CompareBool(elem.tag, it.sought_tag, PyObject.CompareOp.EQ))
                    return elem;
                continue;

            gettext:
                if (text is not PyNone)
                {
                    if (text.IsTrue())
                        return text;
                }
            }
        }

        private const int INIT_PARENT_STACK_SIZE = 8;

        /// <summary>create_elementiter</summary>
        private static PyObject create_elementiter(elementtreestate st, ElementObject self, PyObject tag, bool gettext)
        {
            var it = new ElementIterObject { st = st };
            it.sought_tag = tag;
            it.gettext = gettext;
            it.root_element = self;
            it.parent_stack = new ParentLocator[INIT_PARENT_STACK_SIZE];
            it.parent_stack_used = 0;
            it.parent_stack_size = INIT_PARENT_STACK_SIZE;
            return it;
        }

        private static PyType MakeElementIterType()
        {
            var type = new PyType("_element_iterator", new[] { PyType.ObjectType }, "_elementtree");
            type.MarkSpecHeapType();
            type.MarkImmutableType();
            type.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", type, (self, args, kwargs) => self, 0, 0);
            type.TypeDict["__next__"] = new PyWrapperDescriptor("__next__", type,
                (self, args, kwargs) => ((ElementIterObject)self).IterNext(), 0, 0);
            return type;
        }

        // ------------------------------------------------------------------ the tree builder

        /// <summary>TreeBuilderObject</summary>
        internal sealed class TreeBuilderObject : PyObject
        {
            internal PyObject root; /* root node (first created node) */
            internal PyObject @this; /* current node */
            internal PyObject last; /* most recently created node */
            internal PyObject last_for_tail; /* most recently created node that takes a tail */
            internal PyObject data; /* data collector (string or list), or NULL */
            internal List<PyObject> stack; /* element stack */
            internal long index; /* current stack size (0 means empty) */
            internal PyObject element_factory;
            internal PyObject comment_factory;
            internal PyObject pi_factory;
            /* element tracing */
            internal PyObject events_append; /* the append method of the list of events, or NULL */
            internal PyObject start_event_obj; /* event objects (NULL to ignore) */
            internal PyObject end_event_obj;
            internal PyObject start_ns_event_obj;
            internal PyObject end_ns_event_obj;
            internal PyObject comment_event_obj;
            internal PyObject pi_event_obj;
            internal bool insert_comments;
            internal bool insert_pis;
            internal elementtreestate state;

            internal TreeBuilderObject(PyType type, elementtreestate st)
            {
                state = st;
                if (!ReferenceEquals(type, st.TreeBuilder_Type))
                    SetCustomType(type);
            }

            public override PyType GetPyType() => _customType ?? state.TreeBuilder_Type;
        }

        private static bool TreeBuilder_CheckExact(elementtreestate st, PyObject op) => op.GetPyType() == st.TreeBuilder_Type;

        /// <summary>treebuilder_new</summary>
        private static PyObject treebuilder_new(elementtreestate st, PyType type, PyObject[] args, PyDict kwds)
        {
            var t = new TreeBuilderObject(type, st);
            t.root = null;
            t.@this = PyNone.Instance;
            t.last = PyNone.Instance;
            t.data = null;
            t.element_factory = null;
            t.comment_factory = null;
            t.pi_factory = null;
            t.stack = new List<PyObject>(20);
            for (int i = 0; i < 20; i++)
                t.stack.Add(null);
            t.index = 0;
            t.events_append = null;
            t.start_event_obj = t.end_event_obj = null;
            t.start_ns_event_obj = t.end_ns_event_obj = null;
            t.comment_event_obj = t.pi_event_obj = null;
            t.insert_comments = t.insert_pis = false;
            t.state = st;
            return t;
        }

        /// <summary>_elementtree_TreeBuilder___init___impl</summary>
        private static void TreeBuilder___init__(TreeBuilderObject self, PyObject element_factory, PyObject comment_factory,
                                                 PyObject pi_factory, bool insert_comments, bool insert_pis)
        {
            if (element_factory is not PyNone)
                self.element_factory = element_factory;
            else
                self.element_factory = null;

            if (comment_factory is PyNone)
            {
                elementtreestate st = self.state;
                comment_factory = st.comment_factory;
            }
            if (comment_factory != null)
            {
                self.comment_factory = comment_factory;
                self.insert_comments = insert_comments;
            }
            else
            {
                self.comment_factory = null;
                self.insert_comments = false;
            }

            if (pi_factory is PyNone)
            {
                elementtreestate st = self.state;
                pi_factory = st.pi_factory;
            }
            if (pi_factory != null)
            {
                self.pi_factory = pi_factory;
                self.insert_pis = insert_pis;
            }
            else
            {
                self.pi_factory = null;
                self.insert_pis = false;
            }
        }

        /// <summary>_elementtree__set_factories_impl</summary>
        private static PyObject _set_factories(elementtreestate st, PyObject comment_factory, PyObject pi_factory)
        {

            if (!comment_factory.IsCallable() && comment_factory is not PyNone)
                throw PyTypeError.Create($"Comment factory must be callable, not {TypeName(comment_factory)}");
            if (!pi_factory.IsCallable() && pi_factory is not PyNone)
                throw PyTypeError.Create($"PI factory must be callable, not {TypeName(pi_factory)}");

            PyObject old = PyTuple.From(new PyObject[]
            {
                st.comment_factory ?? PyNone.Instance,
                st.pi_factory ?? PyNone.Instance,
            });

            st.comment_factory = comment_factory is PyNone ? null : comment_factory;
            st.pi_factory = pi_factory is PyNone ? null : pi_factory;

            return old;
        }

        /// <summary>
        /// treebuilder_extend_element_text_or_tail: isText selects element->text, else ->tail
        /// (the PyObject **dest of the C function).
        /// </summary>
        private static void treebuilder_extend_element_text_or_tail(elementtreestate st, PyObject element,
                                                                    TreeBuilderObject self, bool isText, PyString name)
        {
            /* Fast paths for the "almost always" cases. */
            if (Element_CheckExact(st, element))
            {
                var e = (ElementObject)element;
                PyObject dest_obj = isText ? e.text : e.tail;
                bool dest_join = isText ? e.text_join : e.tail_join;
                if (dest_obj is PyNone)
                {
                    bool join = self.data is PyList { IsExact: true };
                    if (isText)
                    {
                        e.text = self.data;
                        e.text_join = join;
                    }
                    else
                    {
                        e.tail = self.data;
                        e.tail_join = join;
                    }
                    self.data = null;
                    return;
                }
                else if (dest_join)
                {
                    // PyList_SetSlice(dest_obj, PY_SSIZE_T_MAX, PY_SSIZE_T_MAX, *data)
                    var list = (PyList)dest_obj;
                    foreach (var item in PyList.SequenceFast(self.data, "can only assign an iterable"))
                        list.Append(item);
                    self.data = null;
                    return;
                }
            }

            /*  Fallback for the non-Element / non-trivial cases. */
            {
                PyObject joined;
                PyObject previous = element.GetAttribute(name.Value);
                joined = list_join(self.data);
                if (previous is not PyNone)
                    joined = PyVM.Instance.ExecuteBinaryOpType(previous, joined, BinaryOpType.ADD);
                element.SetAttribute(name.Value, joined);
                self.data = null;
            }
        }

        /// <summary>treebuilder_flush_data</summary>
        private static void treebuilder_flush_data(TreeBuilderObject self)
        {
            if (self.data == null)
                return;
            elementtreestate st = self.state;
            if (self.last_for_tail == null)
            {
                PyObject element = self.last;
                treebuilder_extend_element_text_or_tail(st, element, self, true, st.str_text);
            }
            else
            {
                PyObject element = self.last_for_tail;
                treebuilder_extend_element_text_or_tail(st, element, self, false, st.str_tail);
            }
        }

        /// <summary>treebuilder_add_subelement</summary>
        private static void treebuilder_add_subelement(elementtreestate st, PyObject element, PyObject child)
        {
            if (Element_CheckExact(st, element))
            {
                ElementObject elem = (ElementObject)element;
                element_add_subelement(st, elem, child);
            }
            else
                CallMethod(element, st.str_append, child);
        }

        /// <summary>treebuilder_append_event</summary>
        private static void treebuilder_append_event(TreeBuilderObject self, PyObject action, PyObject node)
        {
            if (action != null)
            {
                PyObject @event = PyTuple.From(new[] { action, node });
                self.events_append.Call(new[] { @event }, null);
            }
        }

        /// <summary>treebuilder_handle_start</summary>
        private static PyObject treebuilder_handle_start(TreeBuilderObject self, PyObject tag, PyObject attrib)
        {
            PyObject node;
            PyObject @this;
            elementtreestate st = self.state;

            treebuilder_flush_data(self);

            if (self.element_factory == null)
                node = create_new_element(st, tag, attrib);
            else if (attrib == null)
            {
                attrib = new PyDict();
                node = self.element_factory.Call(new[] { tag, attrib }, null);
            }
            else
                node = self.element_factory.Call(new[] { tag, attrib }, null);

            @this = self.@this;
            self.last_for_tail = null;

            if (@this is not PyNone)
                treebuilder_add_subelement(st, @this, node);
            else
            {
                if (self.root != null)
                    throw PyExceptionSlots.Raise(st.parseerror_obj, "multiple elements on top level");
                self.root = node;
            }

            if (self.index < self.stack.Count)
                self.stack[(int)self.index] = @this;
            else
                self.stack.Add(@this);
            self.index++;

            self.@this = node;
            self.last = node;

            treebuilder_append_event(self, self.start_event_obj, node);

            return node;
        }

        /// <summary>treebuilder_handle_data</summary>
        private static PyObject treebuilder_handle_data(TreeBuilderObject self, PyObject data)
        {
            if (self.data == null)
            {
                if (self.last is PyNone)
                {
                    /* ignore calls to data before the first call to start */
                    return PyNone.Instance;
                }
                /* store the first item as is */
                self.data = data;
            }
            else
            {
                /* more than one item; use a list to collect items */
                if (self.data is PyBytes oldBytes && oldBytes.GetPyType() == PyType.BytesType && data is PyBytes newBytes && newBytes.GetPyType() == PyType.BytesType
                    && newBytes.Value.Length == 1)
                {
                    /* XXX this code path unused in Python 3? */
                    /* expat often generates single character data sections; handle
                       the most common case by resizing the existing string... */
                    // (C resizes the bytes in place when it holds the only reference; bytes
                    // are immutable, so a new object of the same value is the same.)
                    var b = new byte[oldBytes.Value.Length + 1];
                    Buffer.BlockCopy(oldBytes.Value, 0, b, 0, oldBytes.Value.Length);
                    b[oldBytes.Value.Length] = newBytes.Value[0];
                    self.data = PyBytes.From(b);
                }
                else if (self.data is PyList { IsExact: true } list)
                    list.Append(data);
                else
                    self.data = new PyList(new[] { self.data, data });
            }

            return PyNone.Instance;
        }

        /// <summary>treebuilder_handle_end</summary>
        private static PyObject treebuilder_handle_end(TreeBuilderObject self, PyObject tag)
        {
            treebuilder_flush_data(self);

            if (self.index == 0)
                throw PyIndexError.Create("pop from empty stack");

            self.last = self.@this;
            self.last_for_tail = self.last;
            self.index--;
            self.@this = self.stack[(int)self.index];

            treebuilder_append_event(self, self.end_event_obj, self.last);

            return self.last;
        }

        /// <summary>treebuilder_handle_comment</summary>
        private static PyObject treebuilder_handle_comment(TreeBuilderObject self, PyObject text)
        {
            PyObject comment;
            PyObject @this;

            treebuilder_flush_data(self);

            if (self.comment_factory != null)
            {
                comment = self.comment_factory.Call(new[] { text }, null);

                @this = self.@this;
                if (self.insert_comments && @this is not PyNone)
                {
                    treebuilder_add_subelement(self.state, @this, comment);
                    self.last_for_tail = comment;
                }
            }
            else
                comment = text;

            if (self.events_append != null && self.comment_event_obj != null)
                treebuilder_append_event(self, self.comment_event_obj, comment);

            return comment;
        }

        /// <summary>treebuilder_handle_pi</summary>
        private static PyObject treebuilder_handle_pi(TreeBuilderObject self, PyObject target, PyObject text)
        {
            PyObject pi;
            PyObject @this;

            treebuilder_flush_data(self);

            if (self.pi_factory != null)
            {
                pi = self.pi_factory.Call(new[] { target, text }, null);

                @this = self.@this;
                if (self.insert_pis && @this is not PyNone)
                {
                    treebuilder_add_subelement(self.state, @this, pi);
                    self.last_for_tail = pi;
                }
            }
            else
                pi = PyTuple.From(new[] { target, text });

            if (self.events_append != null && self.pi_event_obj != null)
                treebuilder_append_event(self, self.pi_event_obj, pi);

            return pi;
        }

        /// <summary>treebuilder_handle_start_ns</summary>
        private static PyObject treebuilder_handle_start_ns(TreeBuilderObject self, PyObject prefix, PyObject uri)
        {
            if (self.events_append != null && self.start_ns_event_obj != null)
            {
                PyObject parcel = PyTuple.From(new[] { prefix, uri });
                treebuilder_append_event(self, self.start_ns_event_obj, parcel);
            }
            return PyNone.Instance;
        }

        /// <summary>treebuilder_handle_end_ns</summary>
        private static PyObject treebuilder_handle_end_ns(TreeBuilderObject self, PyObject prefix)
        {
            if (self.events_append != null && self.end_ns_event_obj != null)
                treebuilder_append_event(self, self.end_ns_event_obj, prefix);
            return PyNone.Instance;
        }

        /// <summary>treebuilder_done</summary>
        private static PyObject treebuilder_done(TreeBuilderObject self)
        {
            PyObject res;

            /* FIXME: check stack size? */

            if (self.root != null)
                res = self.root;
            else
                res = PyNone.Instance;

            return res;
        }

        private static TreeBuilderObject T(PyObject o) => (TreeBuilderObject)o;

        private static PyType MakeTreeBuilderType(elementtreestate st)
        {
            var type = new PyType("TreeBuilder", new[] { PyType.ObjectType }, "xml.etree.ElementTree");
            type.MarkSpecHeapType();
            type.MarkImmutableType();
            type.TypeDict["__module__"] = PyString.From("xml.etree.ElementTree");
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => treebuilder_new(st, (PyType)cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                // _elementtree_TreeBuilder___init__: (element_factory=None, *, comment_factory=None,
                // pi_factory=None, insert_comments=False, insert_pis=False)
                var a = PyArgs.Unpack("TreeBuilder", args, kwargs,
                                      new[] { "element_factory", "comment_factory", "pi_factory", "insert_comments", "insert_pis" },
                                      0, 0, 1);
                TreeBuilder___init__(T(self), a[0] ?? PyNone.Instance, a[1] ?? PyNone.Instance, a[2] ?? PyNone.Instance,
                                     a[3] != null && a[3].IsTrue(), a[4] != null && a[4].IsTrue());
                return PyNone.Instance;
            });
            TypeTables.GenericAttributes(type);
            // treebuilder_methods
            PyArgs.Define(type, "data", PyCall.One, (o, args) => treebuilder_handle_data(T(o), args[0]));
            type.TypeDict["start"] = new PyMethodDescriptor("start", type, (o, args, kwargs) =>
            {
                PyArgs.Positional("start", args, kwargs, 2, 2);
                if (args[1] is not PyDict)
                    throw PyArgs.BadArgument("start", "argument 2", "dict", args[1]);
                return treebuilder_handle_start(T(o), args[0], args[1]);
            }, acceptsKwargs: true);
            PyArgs.Define(type, "end", PyCall.One, (o, args) => treebuilder_handle_end(T(o), args[0]));
            PyArgs.Define(type, "comment", PyCall.One, (o, args) => treebuilder_handle_comment(T(o), args[0]));
            type.TypeDict["pi"] = new PyMethodDescriptor("pi", type, (o, args, kwargs) =>
            {
                PyArgs.Positional("pi", args, kwargs, 1, 2);
                return treebuilder_handle_pi(T(o), args[0], args.Length > 1 ? args[1] : PyNone.Instance);
            }, acceptsKwargs: true);
            PyArgs.Define(type, "close", PyCall.NoArgs, (o, args) => treebuilder_done(T(o)));
            return type;
        }

        // ------------------------------------------------------------------ the parser

        /// <summary>XMLParserObject</summary>
        internal sealed class XMLParserObject : PyObject
        {
            internal XML_Parser parser;
            internal PyObject target;
            internal PyObject entity;
            internal PyObject names;
            internal PyObject handle_start_ns;
            internal PyObject handle_end_ns;
            internal PyObject handle_start;
            internal PyObject handle_data;
            internal PyObject handle_end;
            internal PyObject handle_comment;
            internal PyObject handle_pi;
            internal PyObject handle_doctype;
            internal PyObject handle_close;
            internal elementtreestate state;

            internal XMLParserObject(PyType type, elementtreestate st)
            {
                state = st;
                if (!ReferenceEquals(type, st.XMLParser_Type))
                    SetCustomType(type);
            }

            public override PyType GetPyType() => _customType ?? state.XMLParser_Type;
        }

        private static PyExpat_CAPI EXPAT(elementtreestate st) => st.expat_capi;

        /// <summary>makeuniversal: convert a "{uri}local" style name (expat's "uri}local") to str, cached in names.</summary>
        private static PyObject makeuniversal(XMLParserObject self, XCHARP @string)
        {
            /* convert a UTF-8 tag/attribute name from the expat parser
               to a universal name string */

            int size = @string.Length;
            PyObject key;
            PyObject value;

            /* look the 'raw' name up in the names dictionary */
            var raw = new byte[size];
            Buffer.BlockCopy(@string.a, @string.i, raw, 0, size);
            key = PyBytes.From(raw);

            value = DictGetItemWithError(self.names, key);

            if (value == null)
            {
                /* new name; convert to universal name */
                byte[] tag;
                int i;

                /* look for namespace separator */
                for (i = 0; i < size; i++)
                    if (raw[i] == '}')
                        break;
                if (i != size)
                {
                    /* convert to universal name */
                    tag = new byte[size + 1];
                    tag[0] = (byte)'{';
                    Buffer.BlockCopy(raw, 0, tag, 1, size);
                    size++;
                }
                else
                {
                    /* plain name; use key as tag */
                    tag = raw;
                }

                /* decode universal name */
                value = PyexpatModule.DecodeUTF8(tag, 0, size);

                ((PyDict)self.names).SetItem(key, value);
            }

            return value;
        }

        /// <summary>
        /// expat_set_error: set the ParseError exception with the given parameters. If message is
        /// not NULL, it's used as the error string. Otherwise, the message string is the default for
        /// the given error_code.
        /// </summary>
        private static Exception expat_set_error(elementtreestate st, XML_Error error_code, long line, long column, string message)
        {
            PyObject errmsg = PyString.From($"{message ?? EXPAT(st).ErrorString(error_code)}: line {line}, column {column}");
            PyObject error = st.parseerror_obj.Call(new[] { errmsg }, null);
            error.SetAttribute("code", PyInt.From((long)error_code));
            error.SetAttribute("position", PyTuple.From(new PyObject[] { PyInt.From(line), PyInt.From(column) }));
            return new PythonException((PyBaseException)error);
        }

        // ------------------------------------------------------------------ the expat handlers

        /// <summary>expat_default_handler</summary>
        private static void expat_default_handler(object userData, XCHARP data_in, int data_len)
        {
            XMLParserObject self = (XMLParserObject)userData;
            PyObject key;
            PyObject value;

            if (data_len < 2 || data_in[0] != '&')
                return;

            if (PyErrIndicator.Occurred())
                return;

            try
            {
                key = PyexpatModule.DecodeUTF8(data_in.a, data_in.i + 1, data_len - 2);

                value = DictGetItemWithError(self.entity, key);

                elementtreestate st = self.state;
                if (value != null)
                {
                    if (TreeBuilder_CheckExact(st, self.target))
                        treebuilder_handle_data((TreeBuilderObject)self.target, value);
                    else if (self.handle_data != null)
                        self.handle_data.Call(new[] { value }, null);
                }
                else
                {
                    string text = System.Text.Encoding.UTF8.GetString(data_in.a, data_in.i, data_len < 100 ? data_len : 100);
                    string message = "undefined entity " + text;
                    PyErrIndicator.Set(expat_set_error(st, XML_Error.XML_ERROR_UNDEFINED_ENTITY,
                                                       (long)EXPAT(st).GetErrorLineNumber(self.parser),
                                                       (long)EXPAT(st).GetErrorColumnNumber(self.parser), message));
                }
            }
            catch (Exception e)
            {
                PyErrIndicator.Set(e);
            }
        }

        /// <summary>expat_start_handler</summary>
        private static void expat_start_handler(object userData, XCHARP tag_in, XCHARP[] attrib_in)
        {
            XMLParserObject self = (XMLParserObject)userData;
            PyObject tag;
            PyObject attrib;

            if (PyErrIndicator.Occurred())
                return;

            try
            {
                /* tag name */
                tag = makeuniversal(self, tag_in);

                /* attributes */
                if (!attrib_in[0].IsNull)
                {
                    var d = new PyDict();
                    int k = 0;
                    while (!attrib_in[k].IsNull && !attrib_in[k + 1].IsNull)
                    {
                        PyObject key = makeuniversal(self, attrib_in[k]);
                        PyObject value = PyexpatModule.conv_string_to_unicode(attrib_in[k + 1]);
                        d.SetItem(key, value);
                        k += 2;
                    }
                    attrib = d;
                }
                else
                    attrib = null;

                elementtreestate st = self.state;
                if (TreeBuilder_CheckExact(st, self.target))
                {
                    /* shortcut */
                    treebuilder_handle_start((TreeBuilderObject)self.target, tag, attrib);
                }
                else if (self.handle_start != null)
                {
                    attrib ??= new PyDict();
                    self.handle_start.Call(new[] { tag, attrib }, null);
                }
            }
            catch (Exception e)
            {
                PyErrIndicator.Set(e);
            }
        }

        /// <summary>expat_data_handler</summary>
        private static void expat_data_handler(object userData, XCHARP data_in, int data_len)
        {
            XMLParserObject self = (XMLParserObject)userData;

            if (PyErrIndicator.Occurred())
                return;

            try
            {
                PyObject data = PyexpatModule.DecodeUTF8(data_in.a, data_in.i, data_len);

                elementtreestate st = self.state;
                if (TreeBuilder_CheckExact(st, self.target))
                    /* shortcut */
                    treebuilder_handle_data((TreeBuilderObject)self.target, data);
                else if (self.handle_data != null)
                    self.handle_data.Call(new[] { data }, null);
            }
            catch (Exception e)
            {
                PyErrIndicator.Set(e);
            }
        }

        /// <summary>expat_end_handler</summary>
        private static void expat_end_handler(object userData, XCHARP tag_in)
        {
            XMLParserObject self = (XMLParserObject)userData;

            if (PyErrIndicator.Occurred())
                return;

            try
            {
                elementtreestate st = self.state;
                if (TreeBuilder_CheckExact(st, self.target))
                    /* shortcut */
                    /* the standard tree builder doesn't look at the end tag */
                    treebuilder_handle_end((TreeBuilderObject)self.target, PyNone.Instance);
                else if (self.handle_end != null)
                {
                    PyObject tag = makeuniversal(self, tag_in);
                    self.handle_end.Call(new[] { tag }, null);
                }
            }
            catch (Exception e)
            {
                PyErrIndicator.Set(e);
            }
        }

        private static readonly XCHARP EmptyString = XCHARP.Literal("");

        /// <summary>expat_start_ns_handler</summary>
        private static void expat_start_ns_handler(object userData, XCHARP prefix_in, XCHARP uri_in)
        {
            XMLParserObject self = (XMLParserObject)userData;

            if (PyErrIndicator.Occurred())
                return;

            if (uri_in.IsNull)
                uri_in = EmptyString;
            if (prefix_in.IsNull)
                prefix_in = EmptyString;

            try
            {
                elementtreestate st = self.state;
                if (TreeBuilder_CheckExact(st, self.target))
                {
                    /* shortcut - TreeBuilder does not actually implement .start_ns() */
                    TreeBuilderObject target = (TreeBuilderObject)self.target;

                    if (target.events_append != null && target.start_ns_event_obj != null)
                    {
                        PyObject prefix = PyexpatModule.conv_string_to_unicode(prefix_in);
                        PyObject uri = PyexpatModule.conv_string_to_unicode(uri_in);
                        treebuilder_handle_start_ns(target, prefix, uri);
                    }
                }
                else if (self.handle_start_ns != null)
                {
                    PyObject prefix = PyexpatModule.conv_string_to_unicode(prefix_in);
                    PyObject uri = PyexpatModule.conv_string_to_unicode(uri_in);
                    self.handle_start_ns.Call(new[] { prefix, uri }, null);
                }
            }
            catch (Exception e)
            {
                PyErrIndicator.Set(e);
            }
        }

        /// <summary>expat_end_ns_handler</summary>
        private static void expat_end_ns_handler(object userData, XCHARP prefix_in)
        {
            XMLParserObject self = (XMLParserObject)userData;

            if (PyErrIndicator.Occurred())
                return;

            if (prefix_in.IsNull)
                prefix_in = EmptyString;

            try
            {
                elementtreestate st = self.state;
                if (TreeBuilder_CheckExact(st, self.target))
                {
                    /* shortcut - TreeBuilder does not actually implement .end_ns() */
                    TreeBuilderObject target = (TreeBuilderObject)self.target;

                    if (target.events_append != null && target.end_ns_event_obj != null)
                        treebuilder_handle_end_ns(target, PyNone.Instance);
                }
                else if (self.handle_end_ns != null)
                {
                    PyObject prefix = PyexpatModule.conv_string_to_unicode(prefix_in);
                    self.handle_end_ns.Call(new[] { prefix }, null);
                }
            }
            catch (Exception e)
            {
                PyErrIndicator.Set(e);
            }
        }

        /// <summary>expat_comment_handler</summary>
        private static void expat_comment_handler(object userData, XCHARP comment_in)
        {
            XMLParserObject self = (XMLParserObject)userData;

            if (PyErrIndicator.Occurred())
                return;

            try
            {
                elementtreestate st = self.state;
                if (TreeBuilder_CheckExact(st, self.target))
                {
                    /* shortcut */
                    TreeBuilderObject target = (TreeBuilderObject)self.target;
                    PyObject comment = PyexpatModule.conv_string_to_unicode(comment_in);
                    treebuilder_handle_comment(target, comment);
                }
                else if (self.handle_comment != null)
                {
                    PyObject comment = PyexpatModule.conv_string_to_unicode(comment_in);
                    self.handle_comment.Call(new[] { comment }, null);
                }
            }
            catch (Exception e)
            {
                PyErrIndicator.Set(e);
            }
        }

        /// <summary>expat_start_doctype_handler</summary>
        private static void expat_start_doctype_handler(object userData, XCHARP doctype_name, XCHARP sysid, XCHARP pubid,
                                                        int has_internal_subset)
        {
            XMLParserObject self = (XMLParserObject)userData;
            PyObject doctype_name_obj, sysid_obj, pubid_obj;

            if (PyErrIndicator.Occurred())
                return;

            try
            {
                doctype_name_obj = makeuniversal(self, doctype_name);
                sysid_obj = !sysid.IsNull ? makeuniversal(self, sysid) : PyNone.Instance;
                pubid_obj = !pubid.IsNull ? makeuniversal(self, pubid) : PyNone.Instance;

                /* If the target has a handler for doctype, call it. */
                elementtreestate st = self.state;
                if (self.handle_doctype != null)
                    self.handle_doctype.Call(new[] { doctype_name_obj, pubid_obj, sysid_obj }, null);
                else if (self.LookupAttribute(st.str_doctype) != null)
                {
                    PyWarnings.Warn(PyType.RuntimeWarningType,
                                    "The doctype() method of XMLParser is ignored.  "
                                    + "Define doctype() method on the TreeBuilder target.", 1);
                }
            }
            catch (Exception e)
            {
                PyErrIndicator.Set(e);
            }
        }

        /// <summary>expat_pi_handler</summary>
        private static void expat_pi_handler(object userData, XCHARP target_in, XCHARP data_in)
        {
            XMLParserObject self = (XMLParserObject)userData;

            if (PyErrIndicator.Occurred())
                return;

            try
            {
                elementtreestate st = self.state;
                if (TreeBuilder_CheckExact(st, self.target))
                {
                    /* shortcut */
                    TreeBuilderObject target = (TreeBuilderObject)self.target;

                    if ((target.events_append != null && target.pi_event_obj != null) || target.insert_pis)
                    {
                        PyObject pi_target = PyexpatModule.conv_string_to_unicode(target_in);
                        PyObject data = PyexpatModule.conv_string_to_unicode(data_in);
                        treebuilder_handle_pi(target, pi_target, data);
                    }
                }
                else if (self.handle_pi != null)
                {
                    PyObject pi_target = PyexpatModule.conv_string_to_unicode(target_in);
                    PyObject data = PyexpatModule.conv_string_to_unicode(data_in);
                    self.handle_pi.Call(new[] { pi_target, data }, null);
                }
            }
            catch (Exception e)
            {
                PyErrIndicator.Set(e);
            }
        }

        // ------------------------------------------------------------------ XMLParser

        /// <summary>xmlparser_new</summary>
        private static PyObject xmlparser_new(elementtreestate st, PyType type, PyObject[] args, PyDict kwds)
        {
            var self = new XMLParserObject(type, st);
            self.parser = null;
            self.target = self.entity = self.names = null;
            self.handle_start_ns = self.handle_end_ns = null;
            self.handle_start = self.handle_data = self.handle_end = null;
            self.handle_comment = self.handle_pi = self.handle_close = null;
            self.handle_doctype = null;
            self.state = st;
            return self;
        }

        /// <summary>ignore_attribute_error: PyObject_GetAttrString, NULL for an AttributeError.</summary>
        private static PyObject GetAttrIgnoringAttributeError(PyObject target, string name)
        {
            try
            {
                return target.GetAttribute(name);
            }
            catch (PythonException e) when (e.PyException.GetPyType().IsSubclassOf(PyType.AttributeErrorType))
            {
                return null;
            }
        }

        /// <summary>_elementtree_XMLParser___init___impl</summary>
        private static void XMLParser___init__(XMLParserObject self, PyObject target, string encoding)
        {
            self.entity = new PyDict();
            self.names = new PyDict();

            elementtreestate st = self.state;
            self.parser = EXPAT(st).ParserCreate_MM(XCHARP.FromString(encoding), XCHARP.Literal("}"));
            if (self.parser == null)
            {
                self.entity = null;
                self.names = null;
                throw PyMemoryError.Create();
            }
            // Prefer 16-byte entropy, only expat >= 2.8.0. See gh-149018
            if (EXPAT(st).SetHashSalt16Bytes != null)
                EXPAT(st).SetHashSalt16Bytes(self.parser, PyexpatModule.HashSalt16);

            if (target is PyNone)
                target = treebuilder_new(st, st.TreeBuilder_Type, null, null);
            self.target = target;

            self.handle_start_ns = GetAttrIgnoringAttributeError(target, "start_ns");
            self.handle_end_ns = GetAttrIgnoringAttributeError(target, "end_ns");
            self.handle_start = GetAttrIgnoringAttributeError(target, "start");
            self.handle_data = GetAttrIgnoringAttributeError(target, "data");
            self.handle_end = GetAttrIgnoringAttributeError(target, "end");
            self.handle_comment = GetAttrIgnoringAttributeError(target, "comment");
            self.handle_pi = GetAttrIgnoringAttributeError(target, "pi");
            self.handle_close = GetAttrIgnoringAttributeError(target, "close");
            self.handle_doctype = GetAttrIgnoringAttributeError(target, "doctype");

            /* configure parser */
            EXPAT(st).SetUserData(self.parser, self);
            if (self.handle_start_ns != null || self.handle_end_ns != null)
                EXPAT(st).SetNamespaceDeclHandler(self.parser, expat_start_ns_handler, expat_end_ns_handler);
            EXPAT(st).SetElementHandler(self.parser, expat_start_handler, expat_end_handler);
            EXPAT(st).SetDefaultHandlerExpand(self.parser, expat_default_handler);
            EXPAT(st).SetCharacterDataHandler(self.parser, expat_data_handler);
            if (self.handle_comment != null)
                EXPAT(st).SetCommentHandler(self.parser, expat_comment_handler);
            if (self.handle_pi != null)
                EXPAT(st).SetProcessingInstructionHandler(self.parser, expat_pi_handler);
            EXPAT(st).SetStartDoctypeDeclHandler(self.parser, expat_start_doctype_handler);
            EXPAT(st).SetUnknownEncodingHandler(self.parser, EXPAT(st).DefaultUnknownEncodingHandler, null);
        }

        /// <summary>_check_xmlparser</summary>
        private static void _check_xmlparser(XMLParserObject self)
        {
            if (self.target == null)
                throw PyValueError.Create("XMLParser.__init__() wasn't called");
        }

        /// <summary>expat_parse</summary>
        private static PyObject expat_parse(elementtreestate st, XMLParserObject self, byte[] data, int data_len, bool final)
        {
            XML_Status ok = EXPAT(st).Parse(self.parser, data, 0, data_len, final);

            PyErrIndicator.RaiseIfOccurred();

            if (ok == XML_Status.XML_STATUS_ERROR)
            {
                throw expat_set_error(st, EXPAT(st).GetErrorCode(self.parser),
                                      (long)EXPAT(st).GetErrorLineNumber(self.parser),
                                      (long)EXPAT(st).GetErrorColumnNumber(self.parser), null);
            }

            return PyNone.Instance;
        }

        /// <summary>_elementtree_XMLParser_close_impl</summary>
        private static PyObject XMLParser_close(XMLParserObject self)
        {
            /* end feeding data to parser */

            _check_xmlparser(self);
            elementtreestate st = self.state;
            PyObject res = expat_parse(st, self, Array.Empty<byte>(), 0, true);

            if (TreeBuilder_CheckExact(st, self.target))
                return treebuilder_done((TreeBuilderObject)self.target);
            else if (self.handle_close != null)
                return self.handle_close.Call(Array.Empty<PyObject>(), null);
            else
                return res;
        }

        /// <summary>_elementtree_XMLParser_flush_impl</summary>
        private static PyObject XMLParser_flush(XMLParserObject self)
        {
            _check_xmlparser(self);

            elementtreestate st = self.state;

            if (EXPAT(st).SetReparseDeferralEnabled == null)
                return PyNone.Instance;

            // NOTE: The Expat parser in the C implementation of ElementTree is not
            //       exposed to the outside; as a result we known that reparse deferral
            //       is currently enabled, or we would not even have access to function
            //       XML_SetReparseDeferralEnabled in the first place (which we checked
            //       for, a few lines up).

            EXPAT(st).SetReparseDeferralEnabled(self.parser, false);

            try
            {
                return expat_parse(st, self, Array.Empty<byte>(), 0, false);
            }
            finally
            {
                EXPAT(st).SetReparseDeferralEnabled(self.parser, true);
            }
        }

        /// <summary>_elementtree_XMLParser_feed</summary>
        private static PyObject XMLParser_feed(XMLParserObject self, PyObject data)
        {
            /* feed data to parser */

            _check_xmlparser(self);
            elementtreestate st = self.state;
            if (data is PyString text)
            {
                byte[] data_ptr = UnicodeEncoders.EncodeUtf8(text, null);
                /* Explicitly set UTF-8 encoding. Return code ignored. */
                EXPAT(st).SetEncoding(self.parser, XCHARP.Literal("utf-8"));
                return expat_parse(st, self, data_ptr, data_ptr.Length, false);
            }
            else
            {
                byte[] view = BytesMethods.RequireBuffer(data);
                return expat_parse(st, self, view, view.Length, false);
            }
        }

        /// <summary>_elementtree_XMLParser__parse_whole</summary>
        private static PyObject XMLParser__parse_whole(XMLParserObject self, PyObject file)
        {
            /* (internal) parse the whole input, until end of stream */
            PyObject reader;
            PyObject buffer;
            PyObject res;

            _check_xmlparser(self);

            reader = file.GetAttribute("read");

            elementtreestate st = self.state;
            for (; ; )
            {
                buffer = reader.Call(new PyObject[] { PyInt.From(64 * 1024) }, null);

                if (buffer is PyString { IsExact: true } s)
                {
                    /* A unicode object is encoded into bytes using UTF-8 */
                    if (s.Value.Length == 0)
                        break;
                    buffer = PyBytes.From(UnicodeEncoders.EncodeUtf8(s, "surrogatepass"));
                }
                else if (buffer is not PyBytes || buffer.GetPyType() != PyType.BytesType || ((PyBytes)buffer).Value.Length == 0)
                    break;

                byte[] b = ((PyBytes)buffer).Value;
                expat_parse(st, self, b, b.Length, false);
            }

            res = expat_parse(st, self, Array.Empty<byte>(), 0, true);

            if (TreeBuilder_CheckExact(st, self.target))
                return treebuilder_done((TreeBuilderObject)self.target);

            return res;
        }

        /// <summary>_elementtree_XMLParser__setevents_impl</summary>
        private static PyObject XMLParser__setevents(XMLParserObject self, PyObject events_queue, PyObject events_to_report)
        {
            /* activate element event reporting */
            TreeBuilderObject target;
            PyObject events_append;

            _check_xmlparser(self);
            elementtreestate st = self.state;
            if (!TreeBuilder_CheckExact(st, self.target))
                throw PyTypeError.Create("event handling only supported for ElementTree.TreeBuilder targets");

            target = (TreeBuilderObject)self.target;

            events_append = events_queue.GetAttribute("append");
            target.events_append = events_append;

            /* clear out existing events */
            target.start_event_obj = null;
            target.end_event_obj = null;
            target.start_ns_event_obj = null;
            target.end_ns_event_obj = null;
            target.comment_event_obj = null;
            target.pi_event_obj = null;

            if (events_to_report is PyNone)
            {
                /* default is "end" only */
                target.end_event_obj = PyString.From("end");
                return PyNone.Instance;
            }

            PyObject[] events_seq = PyList.SequenceFast(events_to_report, "events must be a sequence");

            for (int i = 0; i < events_seq.Length; ++i)
            {
                PyObject event_name_obj = events_seq[i];
                string event_name = null;
                if (event_name_obj is PyString str)
                {
                    event_name = PyArgs.AsUtf8CString(str);
                }
                else if (event_name_obj is PyBytes bytes)
                {
                    byte[] v = bytes.Value;
                    int nul = Array.IndexOf(v, (byte)0);
                    event_name = System.Text.Encoding.UTF8.GetString(v, 0, nul < 0 ? v.Length : nul);
                }
                if (event_name == null)
                    throw PyValueError.Create("invalid events sequence");

                if (event_name == "start")
                    target.start_event_obj = event_name_obj;
                else if (event_name == "end")
                    target.end_event_obj = event_name_obj;
                else if (event_name == "start-ns")
                {
                    target.start_ns_event_obj = event_name_obj;
                    EXPAT(st).SetNamespaceDeclHandler(self.parser, expat_start_ns_handler, expat_end_ns_handler);
                }
                else if (event_name == "end-ns")
                {
                    target.end_ns_event_obj = event_name_obj;
                    EXPAT(st).SetNamespaceDeclHandler(self.parser, expat_start_ns_handler, expat_end_ns_handler);
                }
                else if (event_name == "comment")
                {
                    target.comment_event_obj = event_name_obj;
                    EXPAT(st).SetCommentHandler(self.parser, expat_comment_handler);
                }
                else if (event_name == "pi")
                {
                    target.pi_event_obj = event_name_obj;
                    EXPAT(st).SetProcessingInstructionHandler(self.parser, expat_pi_handler);
                }
                else
                    throw PyValueError.Create($"unknown event '{event_name}'");
            }

            return PyNone.Instance;
        }

        private static XMLParserObject X(PyObject o) => (XMLParserObject)o;

        private static PyType MakeXMLParserType(elementtreestate st)
        {
            var type = new PyType("XMLParser", new[] { PyType.ObjectType }, "xml.etree.ElementTree");
            type.MarkSpecHeapType();
            type.MarkImmutableType();
            type.TypeDict["__module__"] = PyString.From("xml.etree.ElementTree");
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => xmlparser_new(st, (PyType)cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                // _elementtree_XMLParser___init__: (*, target=None, encoding=None)
                var a = PyArgs.Unpack("XMLParser", args, kwargs, new[] { "target", "encoding" }, 0, 0, 0);
                PyObject target = a[0] ?? PyNone.Instance;
                string encoding = a[1] == null || a[1] is PyNone ? null
                    : a[1] is PyString ? PyArgs.ClinicStr("XMLParser", "argument 'encoding'", "str or None", a[1])
                    : throw PyArgs.BadArgument("XMLParser", "argument 'encoding'", "str or None", a[1]);
                XMLParser___init__(X(self), target, encoding);
                return PyNone.Instance;
            });
            TypeTables.GenericAttributes(type);
            // xmlparser_methods
            PyArgs.Define(type, "feed", PyCall.One, (o, args) => XMLParser_feed(X(o), args[0]));
            PyArgs.Define(type, "close", PyCall.NoArgs, (o, args) => XMLParser_close(X(o)));
            PyArgs.Define(type, "flush", PyCall.NoArgs, (o, args) => XMLParser_flush(X(o)));
            PyArgs.Define(type, "_parse_whole", PyCall.One, (o, args) => XMLParser__parse_whole(X(o), args[0]));
            type.TypeDict["_setevents"] = new PyMethodDescriptor("_setevents", type, (o, args, kwargs) =>
            {
                PyArgs.Positional("_setevents", args, kwargs, 1, 2);
                return XMLParser__setevents(X(o), args[0], args.Length > 1 ? args[1] : PyNone.Instance);
            }, acceptsKwargs: true);
            // xmlparser_members
            type.TypeDict["entity"] = new PyMemberDescriptor("entity", type, o => X(o).entity ?? PyNone.Instance, readOnly: true);
            type.TypeDict["target"] = new PyMemberDescriptor("target", type, o => X(o).target ?? PyNone.Instance, readOnly: true);
            // xmlparser_getsetlist
            type.TypeDict["version"] = new PyGetSetDescriptor("version", type, o =>
                PyString.From($"Expat {XmlParse.XML_MAJOR_VERSION}.{XmlParse.XML_MINOR_VERSION}.{XmlParse.XML_MICRO_VERSION}"));
            return type;
        }

        // ------------------------------------------------------------------ the module

        /// <summary>module_exec</summary>
        private static void module_exec(PyModule m)
        {
            // A module object of its own each time it is executed (PyType_FromModuleAndSpec):
            // its own types and state, as CPython's per-module state.
            var st = new elementtreestate();
            st.ElementIter_Type = MakeElementIterType();
            st.TreeBuilder_Type = MakeTreeBuilderType(st);
            st.Element_Type = MakeElementType(st);
            st.XMLParser_Type = MakeXMLParserType(st);
            foreach (var t in new[] { st.ElementIter_Type, st.TreeBuilder_Type, st.Element_Type, st.XMLParser_Type })
                PySlotWrappers.AddModuleType(t);
            st.parseerror_obj = PyExceptionSlots.NewException("xml.etree.ElementTree", "ParseError", PyType.SyntaxErrorType);
            elementtreestate s = st;

            s.deepcopy_obj = PyImportSystem.Import("copy").GetAttribute("deepcopy");
            s.elementpath_obj = PyImportSystem.Import("xml.etree.ElementPath");
            s.expat_capsule = PyImportSystem.Import("pyexpat").GetAttribute("expat_CAPI");
            if (s.expat_capsule is not PyCapsule capsule || capsule.Name != "pyexpat.expat_CAPI" || capsule.Pointer is not PyExpat_CAPI capi)
                throw PyValueError.Create("PyCapsule_GetPointer called with incorrect name");
            s.expat_capi = capi;
            if (capi.magic != "pyexpat.expat_CAPI 1.1" || capi.MAJOR_VERSION != XmlParse.XML_MAJOR_VERSION
                || capi.MINOR_VERSION != XmlParse.XML_MINOR_VERSION || capi.MICRO_VERSION != XmlParse.XML_MICRO_VERSION)
                throw PyImportError.Create("pyexpat version is incompatible");

            // _functions
            m.ModuleDict["SubElement"] = new PyBuiltinFunction("SubElement", (args, kwargs) =>
                subelement(s, args, kwargs != null && kwargs.Length() != 0 ? kwargs.Copy() : null))
            { OwnerModule = "_elementtree" };
            m.ModuleDict["_set_factories"] = PyArgs.FastCall("_elementtree", "_set_factories", 2, 2,
                args => _set_factories(s, args[0], args[1]));

            m.ModuleDict["ParseError"] = s.parseerror_obj;
            m.ModuleDict["Element"] = s.Element_Type;
            m.ModuleDict["TreeBuilder"] = s.TreeBuilder_Type;
            m.ModuleDict["XMLParser"] = s.XMLParser_Type;
        }
    }
}
