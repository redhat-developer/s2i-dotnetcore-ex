# MarketPriceGap
Binance market price gap watcher with Discord   
시평갭 2% 이상을 15초동안 유지하면 디스코드로 알림을 보냅니다.

### 사진
![Alt text](/console.PNG)

### 파일
- 모든 파일은 수정 가능하며, 같은 경로에 있어야 합니다
- interval.txt 업데이트 주기에 대한 값입니다
- pair.txt 검색할 계약 목록 입니다 (추가/삭제 가능)
- webhook.txt 디스코드 웹훅 주소입니다 *https://discord.com/api/webhooks/ 이 뒤로 쭉 복사 붙여넣기*   
웹훅 만드는법: https://support.discord.com/hc/ko/articles/228383668-%EC%9B%B9%ED%9B%85%EC%9D%84-%EC%86%8C%EA%B0%9C%ED%95%A9%EB%8B%88%EB%8B%A4

### References
- Binance.NET: https://github.com/JKorf/Binance.Net
- Discord.NET: https://github.com/discord-net/Discord.Net
