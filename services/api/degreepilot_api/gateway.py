import logging

import httpx

for name in ("httpx", "httpcore"):
    logging.getLogger(name).disabled = True


class Gateway:
    def __init__(self, base_url: str, transport=None):
        self.base_url = base_url
        self.transport = transport

    async def call(self, path: str, body=None, timeout=12, limit=131072):
        try:
            async with httpx.AsyncClient(
                base_url=self.base_url, timeout=timeout, follow_redirects=False, transport=self.transport
            ) as client:
                async with client.stream("GET" if body is None else "POST", path, json=body) as response:
                    if response.status_code != 200:
                        return None
                    data = bytearray()
                    async for chunk in response.aiter_bytes():
                        data.extend(chunk)
                        if len(data) > limit:
                            return None
                    import json

                    result = json.loads(data)
                    return result if isinstance(result, dict) else None
        except (httpx.HTTPError, ValueError):
            return None

    async def status(self):
        result = await self.call("advisor/status", timeout=2, limit=4096)
        if result and type(result.get("available")) is bool and isinstance(result.get("reason"), str):
            return {"available": result["available"], "reason": result["reason"][:500]}
        return {
            "available": False,
            "reason": "AI advising is unavailable. Deterministic features remain available.",
        }
