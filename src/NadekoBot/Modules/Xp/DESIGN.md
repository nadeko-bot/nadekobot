## Xp System

- Users in voice and text channels gain xp
- Users gain levels due to activity and receive rewards/recognition
- Server admin can 
  - Set Role Rewards
  - Exclude Users, channels and entire servers

### Todo

- Let users specify server currency rewards
- Server owner should be able to set xp rate on the server
- Remove global xp?

```mermaid
flowchart TD
    classDef haha fill: #550
    A1[Text-Chatting] & A2[Voice-Chatting] --> B[Gain Xp]
    B -.-> Bq{Exclude?}
    Bq -->|No| C[Level Up]
    C --> S1
    C --> S2
    
    subgraph S1[Recognition]
        Dx[Level Up Notification] & Dy[Xp Card]
    end
    
    subgraph S2[lvl up rewards]
        Ex[Role] & Ey[Currency]:::haha
        Ex --> Ex2[Server Benefit]
    end
```