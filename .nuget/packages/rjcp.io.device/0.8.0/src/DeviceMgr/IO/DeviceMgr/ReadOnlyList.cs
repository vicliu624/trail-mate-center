namespace RJCP.IO.DeviceMgr
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    internal class ReadOnlyList<T> : IList<T>
    {
        private readonly IList<T> m_List;

        public ReadOnlyList(IList<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            m_List = list;
        }

        public T this[int index]
        {
            get { return m_List[index]; }
            set { m_List[index] = value; }
        }

        public int Count { get { return m_List.Count; } }

        public bool IsReadOnly { get { return true; } }

        public void Add(T item) { throw new NotSupportedException(); }

        public void Clear() { throw new NotSupportedException(); }

        public bool Contains(T item) { return m_List.Contains(item); }

        public void CopyTo(T[] array, int arrayIndex) { m_List.CopyTo(array, arrayIndex); }

        public IEnumerator<T> GetEnumerator() { return m_List.GetEnumerator(); }

        public int IndexOf(T item) { return m_List.IndexOf(item); }

        public void Insert(int index, T item) { throw new NotSupportedException(); }

        public bool Remove(T item) { throw new NotSupportedException(); }

        public void RemoveAt(int index) { throw new NotSupportedException(); }

        IEnumerator IEnumerable.GetEnumerator() { return m_List.GetEnumerator(); }
    }
}
