namespace SIL.TextEditing
{
    public sealed partial class DocumentEditor
    {
        /// <summary>The character properties text typed now would get (in the active writing system): the run before the caret's, else the
        /// one after it, when the run takes typing, else none. A copy, so the caller can build a run from it; the field draws an input
        /// method's composition in them, since they are what the committed text will have.</summary>
        public PropertyBag TypingProperties()
        {
            return InheritProps(Caret, ActiveWs)?.Clone() ?? new PropertyBag();
        }
    }
}
