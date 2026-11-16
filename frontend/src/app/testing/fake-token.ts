/** For tests: builds a token shaped like the real one (header.payload.signature) with a chosen expiry. */
export function fakeToken(expiresInSeconds = 3600): string {
  const payload = { sub: 'user-1', exp: Math.floor(Date.now() / 1000) + expiresInSeconds };
  return `header.${btoa(JSON.stringify(payload))}.signature`;
}
