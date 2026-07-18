#include <cstdint>
#include <iostream>

extern "C" std::int32_t swift_add(std::int32_t lhs, std::int32_t rhs);

int main() {
    const auto result = swift_add(20, 22);
    std::cout << result << '\n';
    return result == 42 ? 0 : 1;
}
