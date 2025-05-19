mergeInto(LibraryManager.library, {
  SaveToLocalStorage: function(keyPtr, valuePtr) {
    const key = UTF8ToString(keyPtr);
    const value = UTF8ToString(valuePtr);
    localStorage.setItem(key, value);
  },

  LoadFromLocalStorage: function(keyPtr) {
    const key = UTF8ToString(keyPtr);
    const value = localStorage.getItem(key);
    if (!value) return 0;

    const buffer = _malloc(lengthBytesUTF8(value) + 1);
    stringToUTF8(value, buffer, lengthBytesUTF8(value) + 1);
    return buffer;
  },

  RemoveFromLocalStorage: function(keyPtr) {
    const key = UTF8ToString(keyPtr);
    localStorage.removeItem(key);
  }
});