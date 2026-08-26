(() => {
  const nativeFetch = window.fetch.bind(window);
  const apiUrl = input => {
    try {
      const raw = typeof input === 'string' || input instanceof URL ? input : input?.url;
      if (!raw) return null;
      const url = new URL(raw, window.location.href);
      return url.origin === window.location.origin && url.pathname.startsWith('/api/') ? url : null;
    } catch {
      return null;
    }
  };

  const sessionToken = () => localStorage.getItem('jundiai.session');
  const clearSession = () => {
    localStorage.removeItem('jundiai.session');
    localStorage.removeItem('jundiai.role');
    localStorage.removeItem('jundiai.user');
  };

  window.fetch = async (input, init = {}) => {
    const url = apiUrl(input);
    if (!url) return nativeFetch(input, init);

    const headers = new Headers(input instanceof Request ? input.headers : undefined);
    new Headers(init.headers || {}).forEach((value, key) => headers.set(key, value));

    // Nunca permita que frontends legados reabram o bypass demonstrativo.
    headers.delete('X-Demo-Role');
    headers.delete('X-Demo-User');

    const token = sessionToken();
    if (token && !headers.has('Authorization')) headers.set('Authorization', `Bearer ${token}`);

    const response = await nativeFetch(input, { ...init, headers });
    if (response.status === 401 && !url.pathname.startsWith('/api/auth/')) {
      clearSession();
      if (window.location.pathname !== '/login.html') {
        const next = `${window.location.pathname}${window.location.search}${window.location.hash}`;
        window.location.replace(`/login.html?next=${encodeURIComponent(next)}`);
      }
    }
    return response;
  };

  window.JundiaiAuth = Object.freeze({
    sessionToken,
    clearSession,
    authenticated: () => Boolean(sessionToken())
  });
})();
