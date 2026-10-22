# 개발 작업 가이드

Dotori 프로젝트에서 작업할 때 따라야 할 지침들입니다.

## 1. Git 커밋 전략

### 원칙
- 각 작업에 대해 **하나의 깔끔한 커밋** 생성
- 커밋 제목에는 **작업 이름** 포함
- 커밋 본문에는 **작업 내역에 대한 설명 요약** 작성
- 자잘한 수정들은 **모아서 하나의 커밋** 생성

---

## 2. 테스트 추가 원칙

### 필수 테스트 작성
- **모든 새로운 기능**에 대해 테스트 작성
- **검증이 필요한 복잡한 로직**
- **다른 수정에 의해 동작이 변할 수 있는 부분**

### 테스트 작성 시 규칙
- 테스트 실패 시 **테스트를 수정하지 말고 코드를 수정**
  - 예외: SPEC.md의 사양이 정말 변경된 경우만 테스트 수정
- **너무 당연한 기능**은 테스트 생략 가능
- **복잡한 알고리즘이나 엣지 케이스**는 테스트 작성 필수

### 테스트 패턴
```csharp
// 테스트에 실패하면 코드를 수정
[TestMethod]
public void 새로운기능_정상동작()
{
    // Arrange
    var input = CreateInput();

    // Act
    var result = FunctionUnderTest(input);

    // Assert
    Assert.AreEqual(expected, result);
}
```

---

## 3. 문서 동기화

### 핵심 원칙
**SPEC.md의 사양을 변경할 때는 반드시 `docs/` 폴더 내 관련 문서도 함께 수정**

### 변경별 수정 대상

| 변경 사항 | 수정할 파일 |
|----------|-----------|
| CLI 명령어/옵션 추가/변경 | `docs/cli.md` + `docs/dotori.1` |
| .dotori DSL 문법/블록/속성 변경 | `docs/dotori-file.md` |
| 분산 빌드 서버 관련 변경 | `docs/distributed-build.md` |
| 레지스트리 서버 관련 변경 | `deploy/registry/README.md` + 관련 `docs/cli.md` 항목 |
| 새로운 기능 추가 | `docs/examples.md`에 예제 추가 권장 |
| build.sh 빌드 옵션/대상 변경 | `docs/building.md` |
| 프로젝트 자체 빌드 프로세스 변경 | `docs/building.md` + `build.sh` 주석 |

### 변경 예시
```
예: "build-system export 명령어에 새 형식 추가" 작업

SPEC.md에:
  [x] export build-system --format <새형식>

수정할 문서:
  1. docs/cli.md → 새 옵션 설명 추가
  2. docs/dotori.1 → man page 업데이트
  3. docs/examples.md → 사용 예제 추가
```

---

## 4. 작업 흐름 체크리스트

### 새 기능 추가 시
- [ ] `.claude/rules/structure.md`에서 해당 모듈 확인
- [ ] 코드 작성 (`src/Dotori.X/` 폴더)
- [ ] 테스트 작성 (`tests/Dotori.Tests.X/` 폴더)
- [ ] `SPEC.md` 업데이트 (필요 시)
- [ ] 관련 문서 수정 (위의 "변경별 수정 대상" 표 참고)
- [ ] 테스트 실행: `dotnet test`
- [ ] Git 커밋 (의미 있는 메시지)

### 버그 수정 시
- [ ] 버그를 재현하는 테스트 작성
- [ ] 테스트가 실패하는 것 확인
- [ ] 버그 수정
- [ ] 테스트 통과 확인
- [ ] 전체 테스트 실행: `dotnet test`
- [ ] Git 커밋 (버그 설명 포함)

### 문서만 수정 시
- [ ] 해당 문서 파일 수정
- [ ] 다른 관련 문서도 일관성 있는지 확인
- [ ] 링크 및 예제 검증
- [ ] Git 커밋 (문서 수정 내용 설명)

---

## 5. 모범 사례

### 권장하는 커밋
```
제목: "PackageManager: PubGrub 알고리즘 최적화"

- 버전 제약 조건 해석 시 캐싱 추가
- 대규모 의존성 그래프 성능 개선
- 기존 테스트 통과, 새 테스트 5개 추가
```

### 비권장: 피해야 할 커밋
```
제목: "수정"
본문: (없음)

또는

제목: "여러 모듈 수정"
본문: "여러 곳 수정했음"
(이유: 작업 단위가 너무 크고 명확하지 않음)
```

### 권장하는 테스트
```csharp
[TestMethod]
public void DependencyResolver_CircularDependency_ThrowsException()
{
    // 순환 의존성 감지 테스트
    var resolver = new DependencyResolver();
    var deps = CreateCircularDependencies();

    Assert.ThrowsException<DependencyException>(
        () => resolver.Resolve(deps)
    );
}
```

### 비권장: 피해야 할 테스트
```csharp
[TestMethod]
public void Test1()
{
    // 뭘 테스트하는지 불명확
    var result = SomeFunction();
    Assert.IsNotNull(result);
}
```
