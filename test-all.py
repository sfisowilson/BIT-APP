import json, urllib.request
for email, pw in [("admin@afrobotics.co.za","admin123"), ("loverboy.sfiso@gmail.com","editor123"), ("advertiser@afrobotics.co.za","advertiser123")]:
    data = json.dumps({"email": email, "password": pw}).encode()
    req = urllib.request.Request("http://localhost:5000/api/auth/login", data=data, headers={"Content-Type":"application/json"})
    try:
        resp = urllib.request.urlopen(req)
        print(f"OK  {email}")
    except urllib.error.HTTPError as e:
        print(f"FAIL {email}: {e.read().decode()[:80]}")
