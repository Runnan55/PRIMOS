mergeInto(LibraryManager.library, {
  ClearExchangeCodeFromURL: function () {
    if (typeof window !== 'undefined') {
      const url = new URL(window.location);
      url.searchParams.delete('exchangeCode');
      window.history.replaceState({}, document.title, url.toString());
      console.log("[WebGL] exchangeCode removido de la URL");
    }
  }
});